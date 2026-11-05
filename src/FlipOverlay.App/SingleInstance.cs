using System.Security.Cryptography;
using System.Text;

namespace FlipOverlay.App;

/// <summary>Per-user Windows session rendezvous. Secondary launch signals the primary without starting a backend.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private readonly ManualResetEvent stop = new(false);
    private Thread? listener;
    private Action? activate;
    private readonly object gate = new();
    private bool pending;
    private bool disposed;
    public bool IsPrimary { get; }

    public SingleInstance()
    {
        var user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
        var name = "Local\\PoEFlipOverlay." + user;
        mutex = new Mutex(false, name + ".Mutex");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        if (!IsPrimary) { activation.Set(); return; }
        listener = new Thread(Listen) { IsBackground = true, Name = "Overlay activation" };
        listener.Start();
    }

    public void SetActivationHandler(Action handler)
    {
        lock (gate)
        {
            activate = handler;
            if (pending) { pending = false; handler(); }
        }
    }

    private void Listen()
    {
        while (WaitHandle.WaitAny([activation, stop]) == 0)
        {
            lock (gate)
            {
                if (activate is { } handler) handler();
                else pending = true;
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Set();
        listener?.Join();
        activation.Dispose();
        stop.Dispose();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
