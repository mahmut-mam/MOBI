using System.IO;
using System.Text.Json;
using MobiStore.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args[0] == "--preview")
        {
            var app = new MobiStore.Desktop.App(); app.InitializeComponent();
            var window = new MainWindow(true) { Title = "موبي ستور — معاينة التصميم ببيانات توضيحية" };
            var type = typeof(MainWindow);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            type.GetField("login", flags)!.SetValue(window, new MobiStore.Core.LoginResponse("", Guid.Empty, "المدير", true, MobiStore.Core.Access.All));
            type.GetField("company", flags)!.SetValue(window, new MobiStore.Core.SetupStatus(true, "متجر الإكسسوارات", "USD"));
            type.GetMethod("Navigation", flags)!.Invoke(window, null);
            type.GetMethod("Page", flags)!.Invoke(window, new object[] { "المواد والمستودعات", "معاينة مرئية فقط — بيانات توضيحية لمراجعة تنسيق الحقول والجداول." });
            var row = (System.Windows.Controls.WrapPanel)type.GetMethod("Row", flags)!.Invoke(window, null)!;
            var field = type.GetMethod("Field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            field.Invoke(null, new object[] { row, "رمز المادة", "ACC-001", 180d });
            field.Invoke(null, new object[] { row, "اسم المادة", "شاحن سريع USB-C", 260d });
            field.Invoke(null, new object[] { row, "سعر المبيع", "12.50", 150d });
            var body = (System.Windows.Controls.StackPanel)window.FindName("Body");
            var grid = new System.Windows.Controls.DataGrid { AutoGenerateColumns = true, ItemsSource = new[] { new { الرمز = "ACC-001", المادة = "شاحن سريع USB-C", الوحدة = "قطعة", السعر = "12.50 USD" }, new { الرمز = "ACC-002", المادة = "سماعات لاسلكية", الوحدة = "قطعة", السعر = "18.00 USD" } } };
            body.Children.Add(grid);
            // Preview is read-only: navigation is rendered with no active backend.
            ((System.Windows.Controls.Panel)window.FindName("Nav")).IsHitTestVisible = false;
            ((System.Windows.Controls.Menu)window.FindName("SectionMenu")).IsHitTestVisible = false;
            ((System.Windows.Controls.TextBlock)window.FindName("IdentityText")).Text = "متجر الإكسسوارات  •  المدير  •  USD";
            app.Run(window); return;
        }
        if (args[0] == "--brand")
        {
            var app = new MobiStore.Desktop.App(); app.InitializeComponent();
            var mark = (System.Windows.Media.DrawingImage)app.FindResource("BrandMark");
            var visual = new System.Windows.Media.DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawImage(mark, new System.Windows.Rect(0, 0, 256, 256));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(args[1]);
            using var png = new MemoryStream(); encoder.Save(png); var iconBytes = png.ToArray();
            File.WriteAllBytes(Path.Combine(args[1], "brand-mark.png"), iconBytes);
            using var icon = new BinaryWriter(File.Create(Path.Combine(args[1], "mobistore.ico")));
            icon.Write((ushort)0); icon.Write((ushort)1); icon.Write((ushort)1);
            icon.Write((byte)0); icon.Write((byte)0); icon.Write((byte)0); icon.Write((byte)0);
            icon.Write((ushort)1); icon.Write((ushort)32); icon.Write(iconBytes.Length); icon.Write(22); icon.Write(iconBytes);
            Console.WriteLine("Brand mark exported: SVG companion, 256px PNG, Windows icon");
            return;
        }
        var path = args[0]; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var receipt = JsonSerializer.SerializeToElement(new
        {
            number = "RCPT-TEST-20261102", createdUtc = "2026-11-02T11:00:00Z", customer = "عميل اختبار — محمد أحمد",
            manager = "أحمد", currency = "SYP", amount = 150000m, rate = 15000m, amountBase = 10m, balance = 25m,
            baseCurrency = "USD", notes = "دفعة على حساب إكسسوارات الموبايل — إيصال للاختبار فقط"
        });
        ReceiptPdf.Save(path, "متجر موبي ستور — نسخة اختبار", receipt);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 10000 || System.Text.Encoding.ASCII.GetString(bytes, 0, 8) != "%PDF-1.4") throw new Exception("Invalid PDF");
        Console.WriteLine("PASS: Arabic receipt exported as PDF, " + bytes.Length + " bytes");
    }
}
