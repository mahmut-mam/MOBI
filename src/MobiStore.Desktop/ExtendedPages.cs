using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MobiStore.Core;

namespace MobiStore.Desktop;
public partial class MainWindow
{
    private sealed class UiCommand(Action action) : System.Windows.Input.ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private async Task ProductCard(ProductView p)
    {
        var detail = await api.Get<ProductDetails>($"api/products/{p.Id}/details");
        Page("بطاقة المادة — " + p.Name, "الأسعار بالعملة الأساسية. اختر الصورة والتوافق وما يُنشر للعرض.");
        var row = Row(); var code = Field(row, "الرمز", p.Code); var name = Field(row, "الاسم", p.Name, 320);
        var prices = Row(); var retail = Field(prices, "المفرق", p.RetailPrice.ToString(CultureInfo.InvariantCulture)); var wholesale = Field(prices, "الجملة", p.WholesalePrice.ToString(CultureInfo.InvariantCulture));
        var spec = Row(); var brand = Field(spec, "الماركة", detail.Brand); var models = Field(spec, "الهواتف المتوافقة", detail.Models, 350); var color = Field(spec, "اللون", detail.Color);
        var image = Field(Row(), "الصورة المرفوعة أو رابط HTTPS", detail.ImageUrl, 620); image.FlowDirection = FlowDirection.LeftToRight;
        Action(Body, "اختيار صورة من الجهاز", async (_, _) => await Safe(async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "صور PNG وJPEG|*.png;*.jpg;*.jpeg" };
            if (dialog.ShowDialog(this) != true) return;
            var info = new System.IO.FileInfo(dialog.FileName); Rules.Require(info.Length <= 2097152, "الصورة لا تتجاوز 2 ميغابايت");
            var bytes = await System.IO.File.ReadAllBytesAsync(dialog.FileName);
            image.Text = (await api.Post<ImageView>("api/images", new ImageInput(Convert.ToBase64String(bytes), ""))).Url;
        }));
        var publish = new CheckBox { Content = "نشر المادة في كتالوج العرض", IsChecked = detail.Published, IsEnabled = Allowed(Access.Publish) }; Body.Children.Add(publish);
        Action(Body, "حفظ بطاقة المادة", async (_, _) => await Safe(async () =>
        {
            await api.Put($"api/products/{p.Id}/card", new ProductCardInput(new ProductInput(code.Text, name.Text, "", "", 1, Money(retail), Money(wholesale)), new ProductDetails(p.Id, brand.Text, models.Text, color.Text, image.Text.Trim(), publish.IsChecked == true)));
            await Products();
        }));
        Table(Body, p.Units, ("الوحدة", "Name"), ("الباركود", "Barcode"), ("عدد القطع", "Factor"));
    }
    private async Task CustomerPrices(CustomerView customer)
    {
        await LoadCatalog(); Page("أسعار خاصة — " + customer.Name, "السعر للقطعة الأساسية؛ تُحسب بقية الوحدات بمعاملها. السعر الخاص له أولوية على الجملة والمفرق.");
        var row = Row(); var product = Choice(row, "المادة", products, "Name", 350); var price = Field(row, "السعر الخاص", "0");
        Action(Body, "عرض السعر المعتمد", async (_, _) => await Safe(async () => { var p = Selected<ProductView>(product); price.Text = (await api.Get<PriceInput>($"api/prices/{customer.Id}/{p.Id}")).Price.ToString(CultureInfo.InvariantCulture); }));
        Action(Body, "حفظ سعر العميل", async (_, _) => await Safe(async () => { await api.Post<JsonElement>("api/prices", new PriceInput(customer.Id, Selected<ProductView>(product).Id, Money(price))); Note("تم حفظ السعر الخاص بالعميل."); }));
    }
    private async Task Inventory()
    {
        await LoadCatalog(); Page("الجرد وتحويل المستودعات", "التحويل يحافظ على مجموع المخزون. التسوية تقارن الرصيد المتوقع وتُسجل العجز أو الفائض بقيد مستقل.");
        var stocks = await api.Get<List<StockView>>("api/stocks");
        var grid = Table(Body, stocks, ("المادة", "Product"), ("المستودع", "Warehouse"), ("الرصيد بالقطعة", "Quantity"));
        var row = Row(); var from = Choice(row, "من مستودع", warehouses, "Name"); var to = Choice(row, "إلى مستودع", warehouses, "Name");
        var row2 = Row(); var units = Choice(row2, "المادة والوحدة", products.SelectMany(p => p.Units.Select(u => new UnitOption(p, u))).ToList(), "Label", 350); var qty = Field(row2, "كمية التحويل", "1", 150); var reason = Field(row2, "السبب", "تحويل بين المستودعات", 260);
        var transferId = Guid.NewGuid(); StockMoveRequest? transfer = null;
        Action(Body, "تنفيذ التحويل", async (_, _) => await Safe(async () =>
        {
            transfer ??= new(transferId, Selected<WarehouseView>(from).Id, Selected<WarehouseView>(to).Id, Selected<UnitOption>(units).Unit.Id, Money(qty), reason.Text);
            try { await api.Post<OperationResult>("api/stocks/transfer", transfer); await Inventory(); } catch (ApiException) { transfer = null; throw; }
        }));
        if (!login!.IsAdmin) return;
        Note("الجرد: اختر مادة ومستودعًا من الجدول ثم أدخل العدد الفعلي بالقطعة.");
        var countRow = Row(); var count = Field(countRow, "العدد الفعلي", "0"); var note = Field(countRow, "سبب التسوية", "", 350);
        var countId = Guid.NewGuid(); StockCountRequest? countReq = null;
        Action(Body, "اعتماد الجرد للمادة المحددة", async (_, _) => await Safe(async () =>
        {
            Rules.Require(grid.SelectedItem is StockView, "اختر رصيدًا من الجدول"); var s = (StockView)grid.SelectedItem;
            countReq ??= new(countId, s.WarehouseId, s.ProductId, s.Quantity, Money(count), note.Text);
            try { await api.Post<OperationResult>("api/stocks/count", countReq); await Inventory(); } catch (ApiException) { countReq = null; throw; }
        }));
    }
    private async Task Suppliers()
    {
        Page("حسابات الموردين", "الرصيد بالعملة الأساسية: المشتريات ناقص المرتجعات والدفعات. اسم المورد يربط حركاته.");
        var list = await api.Get<List<SupplierView>>("api/suppliers"); var grid = Table(Body, list, ("المورد", "Name"), ("المستحق له", "Balance"));
        Action(Body, "كشف حساب المورد المحدد", async (_, _) => await Safe(async () =>
        {
            Rules.Require(grid.SelectedItem is SupplierView, "اختر المورد"); var supplier = (SupplierView)grid.SelectedItem;
            var ledger = await api.Get<List<SupplierLedgerView>>("api/suppliers/ledger?name=" + Uri.EscapeDataString(supplier.Name));
            Page("كشف حساب — " + supplier.Name, "مدين: مرتجعات ودفعات. دائن: مشتريات.");
            Table(Body, ledger, ("السند", "Number"), ("التاريخ", "CreatedUtc"), ("الحركة", "Kind"), ("مدين", "Debit"), ("دائن", "Credit"));
        }));
        if (!login!.IsAdmin) return;
        var row = Row(); var amount = Field(row, "مبلغ الدفعة", "0"); var currency = Choice(row, "العملة", new[] { "USD", "SYP" }); var rate = Field(row, "ليرات لكل دولار", (await api.Get<RateInput>("api/exchange-rate")).Rate.ToString(CultureInfo.InvariantCulture));
        var notes = Field(Row(), "بيان الدفع", "", 450); var id = Guid.NewGuid(); SupplierPaymentRequest? pending = null;
        Action(Body, "دفع من صندوق المدير", async (_, _) => await Safe(async () =>
        {
            Rules.Require(grid.SelectedItem is SupplierView, "اختر المورد");
            var shift = (await api.Get<List<SessionView>>("api/sessions")).SingleOrDefault(x => x.UserId == login.UserId); Rules.Require(shift is not null, "افتح صندوقك أولًا");
            pending ??= new(id, shift!.Id, ((SupplierView)grid.SelectedItem).Name, Money(amount), Selected<string>(currency), Money(rate), notes.Text);
            try { await api.Post<OperationResult>("api/suppliers/pay", pending); await Suppliers(); } catch (ApiException) { pending = null; throw; }
        }));
    }
    private async Task PurchaseReturns()
    {
        await LoadCatalog(); var purchases = await api.Get<JsonElement[]>("api/purchases");
        Page("مرتجعات المشتريات", "يرتبط المرتجع بالفاتورة الأصلية وسعر صرفها، ويخفض المخزون ورصيد المورد. لا ينشئ قبضًا نقديًا.");
        var chosen = Choice(Row(), "الفاتورة الأصلية", purchases.Select(x => new PurchaseOption(x.GetProperty("id").GetGuid(), x.GetProperty("number").GetString()!)).ToList(), "Number", 600);
        Action(Body, "فتح بنود الفاتورة", async (_, _) => await Safe(async () =>
        {
            var option = Selected<PurchaseOption>(chosen); var purchase = await api.Get<PurchaseRequest>($"api/purchases/{option.Id}");
            Page("مرتجع شراء — " + purchase.Supplier, option.Number);
            var options = purchase.Lines.Select(l => { var p = products.Single(x => x.Units.Any(u => u.Id == l.UnitId)); var u = p.Units.Single(x => x.Id == l.UnitId); return new PurchaseReturnLine(l.UnitId, p.Name, u.Name, l.Quantity, l.UnitCost); }).ToList();
            var grid = Table(Body, options, ("المادة", "Name"), ("الوحدة", "Unit"), ("كمية الشراء", "Quantity"), ("تكلفة الوحدة", "Cost"));
            var qty = Field(Row(), "كمية المرتجع للسطر المحدد", "1", 300); var id = Guid.NewGuid(); PurchaseReturnRequest? pending = null;
            Action(Body, "اعتماد المرتجع", async (_, _) => await Safe(async () =>
            {
                Rules.Require(grid.SelectedItem is PurchaseReturnLine, "اختر السطر"); var line = (PurchaseReturnLine)grid.SelectedItem;
                pending ??= new(id, option.Id, [new(line.UnitId, Money(qty), 0)]);
                try { await api.Post<OperationResult>("api/purchases/return", pending); await PurchaseReturns(); } catch (ApiException) { pending = null; throw; }
            }));
        }));
    }
    private record PurchaseOption(Guid Id, string Number);
    private record PurchaseReturnLine(Guid UnitId, string Name, string Unit, decimal Quantity, decimal Cost);
    private async Task Maintenance()
    {
        Page("النسخ الاحتياطي وسجل العمليات", "إدارة سعر الصرف ومراجعة الحركات والنسخ الاحتياطي من جهاز السيرفر.");
        var rate = Field(Row(), "سعر الصرف الحالي: ليرة لكل دولار", (await api.Get<RateInput>("api/exchange-rate")).Rate.ToString(CultureInfo.InvariantCulture), 300);
        Action(Body, "حفظ سعر الصرف", async (_, _) => await Safe(async () => { await api.Post<JsonElement>("api/exchange-rate", new RateInput(Money(rate))); Note("تم حفظ السعر. الفواتير القديمة تحتفظ بأسعارها."); }));
        var backup = await api.Get<JsonElement>("api/backups");
        var settings = await api.Get<JsonElement>("api/backups/settings");
        var pathRow = Row(); var folder = Field(pathRow, "مجلد النسخ الاحتياطي على السيرفر", settings.GetProperty("directory").GetString() ?? "", 550); folder.FlowDirection = FlowDirection.LeftToRight;
        Action(pathRow, "اختيار مجلد", (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "اختر مجلد النسخ الاحتياطي على جهاز السيرفر", Multiselect = false };
            if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName;
        });
        Action(Body, "حفظ مسار النسخ الاحتياطي", async (_, _) => await Safe(async () =>
        { await api.Post<JsonElement>("api/backups/settings", new { directory = folder.Text.Trim() }); Note("تم حفظ المسار. إنشاء النسخة التالية سيستخدم هذا المجلد."); }));
        Note("اكتب المسار يدويًا أو اختره من جهاز السيرفر. يجب أن يكون المجلد موجودًا ولحساب خدمة SQL صلاحية الكتابة فيه. تركه فارغًا يستخدم مجلد SQL الافتراضي.");
        Note(backup.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "لا توجد نسخة احتياطية مسجلة بعد." : "آخر نسخة: " + backup.GetProperty("createdUtc").GetDateTime().ToLocalTime().ToString("g"));
        Action(Body, "إنشاء نسخة احتياطية وفحصها", async (_, _) => await Safe(async () =>
        {
            var result = await api.PostLong<JsonElement>("api/backups"); Note("تم إنشاء النسخة وفحصها: " + result.GetProperty("file").GetString());
        }));
        Action(Body, "اختبار الاستعادة إلى قاعدة منفصلة", async (_, _) => await Safe(async () =>
        { var result = await api.PostLong<JsonElement>("api/backups/restore-test"); Note("نجحت الاستعادة التجريبية: " + result.GetProperty("database").GetString()); }));
        string PageUrl(string page) => new Uri(api.Client.BaseAddress!, page).ToString() + "?database=" + Uri.EscapeDataString(api.Client.DefaultRequestHeaders.GetValues("X-MobiStore-Database").Single());
        Action(Body, "فتح كتالوج العرض", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PageUrl("catalog")) { UseShellExecute = true }));
        Action(Body, "فتح واجهة الهاتف", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PageUrl("mobile")) { UseShellExecute = true }));
        var audit = await api.Get<JsonElement[]>("api/audit");
        var auditGrid = Table(Body, audit.Select(x => new AuditRow(x.GetProperty("id").GetGuid(), x.GetProperty("number").GetString()!, x.GetProperty("kind").GetString()!, x.GetProperty("user").GetString()!, x.GetProperty("createdUtc").GetDateTime().ToLocalTime())).ToArray(), ("الرقم", "Number"), ("الحركة", "Kind"), ("المستخدم", "User"), ("التوقيت", "Date"));
        Action(Body, "تفاصيل العملية وسبب التعديل", async (_, _) => await Safe(async () =>
        {
            Rules.Require(auditGrid.SelectedItem is AuditRow, "اختر عملية من السجل"); var selected = (AuditRow)auditGrid.SelectedItem;
            var record = await api.Get<JsonElement>($"api/audit/{selected.Id}"); Page("تفاصيل العملية", selected.Number);
            Body.Children.Add(new TextBox { IsReadOnly = true, Text = JsonSerializer.Serialize(JsonDocument.Parse(record.GetProperty("details").GetString()!), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight, MinHeight = 250 });
        }));
    }
    private record AuditRow(Guid Id, string Number, string Kind, string User, DateTime Date);
    private async Task EditUser(UserView user)
    {
        var settings = await api.Get<UserUpdate>($"api/users/{user.Id}"); Page("صلاحيات — " + user.DisplayName, "يُسجل التعديل وسببه وتُنهي الخدمة جلسات المستخدم لتطبيق الصلاحيات الجديدة.");
        var row = Row(); var name = Field(row, "اسم العرض", settings.DisplayName); var max = Field(row, "أقصى خصم %", settings.MaxDiscountPercent.ToString(CultureInfo.InvariantCulture));
        var active = new CheckBox { Content = "الحساب فعال", IsChecked = settings.Active, IsEnabled = !user.IsAdmin }; Body.Children.Add(active);
        var rights = Row(); var boxes = new Dictionary<string, CheckBox>();
        foreach (var permission in Access.All) { var box = new CheckBox { Content = PermissionName(permission), IsChecked = settings.Permissions.Contains(permission), Width = 240 }; rights.Children.Add(box); boxes[permission] = box; }
        var reason = Field(Row(), "سبب التعديل", "", 500);
        Action(Body, "حفظ الصلاحيات", async (_, _) => await Safe(async () =>
        {
            await api.Put($"api/users/{user.Id}", new UserUpdate(name.Text, active.IsChecked == true, boxes.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray(), Money(max), reason.Text));
            if (user.Id == login!.UserId) { api.Client.DefaultRequestHeaders.Authorization = null; login = null; await Connect(); } else await Users();
        }));
    }
    private static string PermissionName(string permission) => permission switch
    {
        Access.Catalog => "إدارة المواد والمستودعات", Access.Customers => "إدارة العملاء", Access.Purchase => "المشتريات والموردون", Access.Sale => "إنشاء مبيعات",
        Access.Discount => "الخصومات وتخفيض الأسعار", Access.Credit => "البيع الآجل", Access.Return => "المرتجعات", Access.Costs => "عرض التكلفة", Access.Accounts => "عرض الحسابات",
        Access.Close => "إغلاق الصناديق", Access.Users => "المستخدمون", Access.Hr => "الموارد البشرية", Access.Publish => "نشر مواد الكتالوج", Access.Collect => "تحصيل العملاء", _ => permission
    };
    private async Task Announcements()
    {
        await LoadCatalog(); Page("إعلان المواد الجديدة", "اختر تاريخ الإدخال والمواد والمستلمين، ثم عاين الإعلان. الرسالة تتضمن السعر وروابط الصور دون كميات.");
        var since = Field(Row(), "مواد أُدخلت منذ yyyy-MM-dd", DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd"), 300);
        var productsPanel = new WrapPanel(); Body.Children.Add(productsPanel);
        var selectedProducts = new Dictionary<Guid, (JsonElement Item, CheckBox Box)>();
        async Task Load()
        {
            var date = DateTime.ParseExact(since.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToUniversalTime();
            var items = await api.Get<JsonElement[]>("api/announcements?since=" + Uri.EscapeDataString(date.ToString("O")));
            productsPanel.Children.Clear(); selectedProducts.Clear();
            foreach (var item in items) { var box = new CheckBox { Content = item.GetProperty("name").GetString(), Width = 280 }; productsPanel.Children.Add(box); selectedProducts[item.GetProperty("id").GetGuid()] = (item, box); }
        }
        Action(Body, "تحديث المواد الجديدة", async (_, _) => await Safe(Load)); await Load();
        Action(Body, "تحديد كل المواد", (_, _) => { foreach (var item in selectedProducts.Values) item.Box.IsChecked = true; });
        var recipients = new Dictionary<CustomerView, CheckBox>(); var people = Row();
        foreach (var customer in customers.Where(x => !string.IsNullOrWhiteSpace(x.Phone)))
        { var box = new CheckBox { Content = customer.Name + " — " + customer.Phone, Width = 300 }; people.Children.Add(box); recipients[customer] = box; }
        Action(Body, "تحديد كل المستلمين", (_, _) => { foreach (var box in recipients.Values) box.IsChecked = true; });
        Action(Body, "إلغاء تحديد المستلمين", (_, _) => { foreach (var box in recipients.Values) box.IsChecked = false; });
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150 }; Body.Children.Add(preview);
        var links = new WrapPanel(); Body.Children.Add(links);
        Action(Body, "معاينة الإعلان والمستلمين", (_, _) =>
        {
            links.Children.Clear(); var items = selectedProducts.Values.Where(x => x.Box.IsChecked == true).Select(x => x.Item).ToArray();
            if (items.Length == 0) { StatusText.Text = "اختر مادة جديدة على الأقل"; return; }
            var message = company.ShopName + " — وصل حديثًا\n\n" + string.Join("\n\n", items.Select(x => x.GetProperty("name").GetString() + "\n" + x.GetProperty("price").GetDecimal().ToString("N2") + " " + company.BaseCurrency + (x.TryGetProperty("imageUrl", out var url) && url.ValueKind == JsonValueKind.String ? "\n" + url.GetString() : "")));
            preview.Text = message;
            foreach (var customer in recipients.Where(x => x.Value.IsChecked == true).Select(x => x.Key))
            {
                var phone = new string(customer.Phone.Where(char.IsAsciiDigit).ToArray()); if (phone.StartsWith("00")) phone = phone[2..];
                if (phone.Length is < 8 or > 15 || phone.StartsWith('0')) { Note("أدخل رقم " + customer.Name + " مع رمز الدولة قبل إرساله"); continue; }
                var target = "https://wa.me/" + phone + "?text=" + Uri.EscapeDataString(message);
                Action(links, "فتح واتساب — " + customer.Name, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }));
            }
        });
        Note("فتح واتساب يجهز الرسالة فقط. تضغط الإرسال بنفسك داخل واتساب لكل مستلم؛ الإرسال الآلي عبر مزود واتساب لم يُربط بعد.");
    }
}
