using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MobiStore.Desktop;

// One-page raster PDF: WPF performs Arabic shaping, then a JPEG is embedded in a standard PDF.
// Receipt text is readable in Arabic but not searchable/selectable in this first export implementation.
public static class ReceiptPdf
{
    public static void Save(string path, string shop, JsonElement r)
    {
        var page = new StackPanel { Width = 794, Height = 1123, Background = Brushes.White, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0) };
        void Text(string text, double size = 20, bool bold = false) => page.Children.Add(new TextBlock
        {
            Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(55, 12, 55, 4)
        });
        string S(string key) => r.GetProperty(key).ToString();
        Text(shop, 32, true); Text("إيصال قبض من عميل", 28, true);
        Text("الرقم: " + S("number"), 13); Text("التاريخ: " + S("createdUtc"), 16);
        Text("العميل: " + S("customer")); Text("استلم المدير: " + S("manager"));
        Text($"المبلغ المقبوض: {S("amount")} {S("currency")}", 27, true);
        Text("سعر الصرف — ليرة لكل دولار: " + S("rate"));
        Text($"القيمة الأساسية: {S("amountBase")} {S("baseCurrency")}");
        Text($"الرصيد بعد القبض: {S("balance")} {S("baseCurrency")}"); Text("البيان: " + S("notes"));
        Text("سُجّلت الدفعة على صندوق المدير. هذا الإيصال لا يسجل دفعة إضافية.", 16);
        Text("توقيع المستلم: __________________", 18);
        page.Measure(new Size(794, 1123)); page.Arrange(new Rect(0, 0, 794, 1123)); page.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1588, 2246, 192, 192, PixelFormats.Pbgra32); bitmap.Render(page);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var image = new MemoryStream(); encoder.Save(image); var jpg = image.ToArray();
        using var file = File.Create(path); var offsets = new List<long> { 0 };
        void Write(string s) { var bytes = Encoding.ASCII.GetBytes(s); file.Write(bytes); }
        void Object(int id, string content) { offsets.Add(file.Position); Write($"{id} 0 obj\n{content}\nendobj\n"); }
        Write("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
        offsets.Add(file.Position); Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width 1588 /Height 2246 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpg.Length} >>\nstream\n"); file.Write(jpg); Write("\nendstream\nendobj\n");
        const string content = "q 595 0 0 842 0 0 cm /Im0 Do Q\n"; Object(5, $"<< /Length {content.Length} >>\nstream\n{content}endstream");
        var start = file.Position; Write("xref\n0 6\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10") + " 00000 n \n");
        Write($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");
    }
}
