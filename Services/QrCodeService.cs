using QRCoder;

namespace Inventario.Services;

public class QrCodeService
{
    public string CreateSvg(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data);
        return qr.GetGraphic(4);
    }
}
