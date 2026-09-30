using Foundation;
using UIKit;
using UserNotifications;

namespace Alpixa.App.Services;

public sealed partial class PlatformService
{
    private NSObject? _activity;
    private int _authorizationRequested;

    public override void SetKeepAwake(bool keepAwake)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (keepAwake && _activity is null)
            {
                _activity = NSProcessInfo.ProcessInfo.BeginActivity(
                    NSActivityOptions.IdleSystemSleepDisabled | NSActivityOptions.UserInitiated,
                    "Alpixa e-posta gönderimi");
            }
            else if (!keepAwake && _activity is not null)
            {
                NSProcessInfo.ProcessInfo.EndActivity(_activity);
                _activity = null;
            }
        });

    public override void ShowNotification(string title, string message)
    {
        EnsureAuthorization();
        var content = new UNMutableNotificationContent { Title = title, Body = message };
        var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null);
        UNUserNotificationCenter.Current.AddNotificationRequest(request, _ => { });
    }

    public override void SetProgress(double? fraction)
    {
        EnsureAuthorization();
        var badge = fraction is null ? 0 : (int)Math.Round(fraction.Value * 100);
        UNUserNotificationCenter.Current.SetBadgeCount(badge, _ => { });
    }

    public override void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        MainThread.BeginInvokeOnMainThread(() =>
            UIApplication.SharedApplication.OpenUrl(NSUrl.FromFilename(path), new UIApplicationOpenUrlOptions(), null));
    }

    public override async Task<IReadOnlyList<string>> GetDroppedFilePathsAsync(object platformDropArgs)
    {
        if (platformDropArgs is not PlatformDropEventArgs { DropSession: { } session }) return [];
        var result = new List<string>();
        foreach (var item in session.Items)
        {
            var completion = new TaskCompletionSource<string?>();
            item.ItemProvider.LoadFileRepresentation("public.data", (url, error) =>
            {
                if (url?.Path is null || error is not null)
                {
                    completion.TrySetResult(null);
                    return;
                }
                var target = Path.Combine(Path.GetTempPath(), url.LastPathComponent ?? Path.GetFileName(url.Path));
                File.Copy(url.Path, target, overwrite: true);
                completion.TrySetResult(target);
            });
            if (await completion.Task is { } path) result.Add(path);
        }
        return result;
    }

    private void EnsureAuthorization()
    {
        if (Interlocked.Exchange(ref _authorizationRequested, 1) == 1) return;
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound, (_, _) => { });
    }
}
