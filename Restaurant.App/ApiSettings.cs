namespace Restaurant.App;

public static class ApiSettings
{
    private const string LocalHostBaseUrl = "http://127.0.0.1:5123";
    private const string DeviceBaseUrl = "http://100.103.230.85:5123";

    public static string BaseUrl =>
        DeviceInfo.Platform == DevicePlatform.Android ||
        DeviceInfo.Platform == DevicePlatform.iOS ||
        DeviceInfo.Platform == DevicePlatform.MacCatalyst ||
        DeviceInfo.Platform == DevicePlatform.WinUI
            ? DeviceBaseUrl
            : LocalHostBaseUrl;
}
