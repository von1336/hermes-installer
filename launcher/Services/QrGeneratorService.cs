using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using HermesLauncher.Models;
using QRCoder;

namespace HermesLauncher.Services;

public class QrGeneratorService
{
    public static (string DeepLink, string Base64Code, ConnectPayloadDto Payload) BuildConnectPayload(
        string connectHost,
        string apiKey,
        string password,
        string tailscaleIp = "",
        int gatewayPort = 8642,
        int workspacePort = 3000,
        int dashboardPort = 9119,
        int? expireHours = 24)
    {
        long? expEpoch = null;
        if (expireHours.HasValue && expireHours.Value > 0)
        {
            var expTime = DateTime.UtcNow.AddHours(expireHours.Value);
            expEpoch = (long)(expTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        var host = string.IsNullOrWhiteSpace(connectHost) ? "127.0.0.1" : connectHost;
        var payload = new ConnectPayloadDto
        {
            Version = 1,
            Gateway = $"http://{host}:{gatewayPort}",
            Dashboard = $"http://{host}:{workspacePort}",
            AgentDashboard = $"http://{host}:{dashboardPort}",
            Workspace = $"http://{host}:{workspacePort}",
            ApiKey = apiKey,
            Password = password,
            TailscaleIp = tailscaleIp,
            ExpiryEpoch = expEpoch
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var deepLink = $"hermes://connect?data={base64}";
        return (deepLink, base64, payload);
    }

    public static BitmapSource GenerateQrImage(string content, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(data);
        var qrBytes = qrCode.GetGraphic(pixelsPerModule, new byte[] { 255, 255, 255, 255 }, new byte[] { 7, 9, 14, 255 }); // Dark modules on clean white

        using var ms = new MemoryStream(qrBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }
}
