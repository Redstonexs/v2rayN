using System.Reactive.Concurrency;

namespace ServiceLib.Common;

// Compatibility shim for code paths that still expect the newer scheduler helper.
public static class RxSchedulers
{
    public static IScheduler MainThreadScheduler => RxApp.MainThreadScheduler;
}
