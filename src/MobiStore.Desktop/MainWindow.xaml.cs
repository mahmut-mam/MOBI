using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MobiStore.Core;

namespace MobiStore.Desktop;

public partial class MainWindow : Window
{
    private Api api = new(Environment.GetEnvironmentVariable("MOBISTORE_SERVER") ?? "http://127.0.0.1:5188");
    private LoginResponse? login;
    private SetupStatus company = new(false, "", "USD");
    private bool busy;
    private List<ProductView> products = [];
    private List<CustomerView> customers = [];
    private List<WarehouseView> warehouses = [];
    public MainWindow() : this(false) { }
    public MainWindow(bool designPreview)
    {
        InitializeComponent();
        if (designPreview) return;
        Loaded += async (_, _) =>
        {
            var selector = new DatabaseWindow(api.Client.BaseAddress!.ToString());
            if (selector.ShowDialog() != true) { Close(); return; }
            api = selector.SelectedApi!;
            Title = "موبي ستور — " + selector.SelectedName;
            await Safe(Connect);
        };
    }
    private bool Allowed(string permission) => login is { IsAdmin: true } || login?.Permissions.Contains(permission) == true;
    private async Task Safe(Func<Task> action)
    {
        if (busy) return;
        busy = true; IsEnabled = false; StatusText.Text = "جارٍ تنفيذ الطلب…";
        try { await action(); StatusText.Text = "جاهز • " + DateTime.Now.ToString("HH:mm"); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException or FormatException or RuleException or IOException)
        { StatusText.Text = ex is HttpRequestException ? "تعذر الاتصال بالخادم. تحقق من تشغيله وعنوانه" : ex.Message; MessageBox.Show(StatusText.Text, "موبي ستور", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { busy = false; IsEnabled = true; }
    }
    private void Page(string title, string subtitle)
    {
        Body.Children.Clear(); InputBindings.Clear();
        BreadcrumbText.Text = "الرئيسية  /  " + title;
        BusinessDate.Text = DateTime.Now.ToString("dddd، dd MMMM yyyy", CultureInfo.GetCultureInfo("ar-SY"));
        Body.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 8) });
        Body.Children.Add(new TextBlock { Text = subtitle, Foreground = Color("#7C8A9E"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 26) });
    }
    private static SolidColorBrush Color(string hex) => new((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
    private static TextBox Field(Panel parent, string label, string value = "", double width = 210)
    {
        var group = new StackPanel { Width = width, Margin = new(0, 0, double.IsNaN(width) ? 0 : 14, 0) };
        group.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Color("#586A82") }); var box = new TextBox { Text = value }; group.Children.Add(box); parent.Children.Add(group); return box;
    }
    private static ComboBox Choice<T>(Panel parent, string label, IEnumerable<T> items, string display = "", double width = 210)
    {
        var group = new StackPanel { Width = width, Margin = new(0, 0, double.IsNaN(width) ? 0 : 14, 0) }; group.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Color("#586A82") });
        var box = new ComboBox { ItemsSource = items, DisplayMemberPath = display, SelectedIndex = 0 }; group.Children.Add(box); parent.Children.Add(group); return box;
    }
    private static Button Action(Panel parent, string text, RoutedEventHandler handler)
    { var button = new Button { Content = text }; button.Click += handler; parent.Children.Add(button); return button; }
    private WrapPanel Row()
    {
        var panel = new WrapPanel();
        Body.Children.Add(new Border { Child = panel, Background = Color("#F7F9FC"), CornerRadius = new(12), Padding = new(17, 13, 3, 2), Margin = new(0, 6, 0, 16) });
        return panel;
    }
    private static decimal Money(TextBox box) => decimal.TryParse(box.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException("أدخل رقمًا صحيحًا باستخدام النقطة للكسور");
    private static T Selected<T>(ComboBox box) => box.SelectedItem is T item ? item : throw new RuleException("اختر السجل المطلوب");
    private static DataGrid Table<T>(Panel parent, IEnumerable<T> rows, params (string Title, string Path)[] columns)
    {
        var grid = new DataGrid { ItemsSource = rows };
        foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = c.Title, Binding = new Binding(c.Path), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        parent.Children.Add(new Border { Child = grid, Background = Brushes.White, BorderBrush = Color("#E3EAF2"), BorderThickness = new(1), CornerRadius = new(11), Padding = new(1), Margin = new(0, 12, 0, 18), ClipToBounds = true }); return grid;
    }
    private void Note(string text) => Body.Children.Add(new Border { Background = Color("#EAF7F3"), CornerRadius = new(9), Padding = new(16, 12, 16, 12), Margin = new(0, 12, 0, 12), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Color("#268674") } });
    private async Task Connect()
    {
        company = await api.Get<SetupStatus>("api/setup");
        Shell.Visibility = Visibility.Collapsed;
        AuthSurface.Visibility = Visibility.Visible;
        AuthTitle.Text = company.Installed ? "أهلًا بعودتك" : "لنبدأ حكاية محلك";
        AuthSubtitle.Text = company.Installed ? $"سجّل الدخول إلى {company.ShopName} لمتابعة أعمالك." : "أدخل بيانات المنشأة وأنشئ حساب المدير الأول.";
        AuthNotice.Text = company.Installed ? "حساب مستقل لكل مستخدم. تجربة مترابطة لكل فريقك." : "اختر العملة الأساسية بعناية. كلمة المرور 12 حرفًا على الأقل.";
        Nav.Children.Clear();
        AuthForm.Children.Clear();
        var form = AuthForm;
        TextBox? shop = null, display = null; ComboBox? currency = null;
        if (!company.Installed)
        { shop = Field(form, "اسم المحل", "", double.NaN); display = Field(form, "اسم المدير", "", double.NaN); currency = Choice(form, "العملة الأساسية", new[] { "USD", "SYP" }, width: double.NaN); }
        var username = Field(form, "اسم المستخدم", "", double.NaN);
        username.FlowDirection = FlowDirection.LeftToRight;
        form.Children.Add(new TextBlock { Text = "كلمة المرور", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Color("#586A82") }); var password = new PasswordBox { HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.LeftToRight }; form.Children.Add(password);
        var submit = Action(form, company.Installed ? "تسجيل الدخول  ←" : "إنشاء مساحة العمل  ←", async (_, _) => await Safe(async () =>
        {
            if (!company.Installed)
            {
                await api.Post<JsonElement>("api/setup", new SetupRequest(shop!.Text, Selected<string>(currency!), username.Text, display!.Text, password.Password));
                password.Clear(); await Connect(); return;
            }
            login = await api.Post<LoginResponse>("api/login", new LoginRequest(username.Text, password.Password)); password.Clear();
            api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            AuthNotice.Text = "جارٍ تحميل الصلاحيات وإعدادات المنشأة من السيرفر…";
            var session = await api.Get<SessionState>("api/session");
            login = session.User with { Token = login.Token }; company = session.Company;
            AuthNotice.Text = "جارٍ تحميل المواد والعملاء والمستودعات…";
            await LoadCatalog();
            IdentityText.Text = $"{company.ShopName}  •  {login.DisplayName}  •  {company.BaseCurrency}";
            Navigation();
            if (Allowed(Access.Accounts)) await Dashboard(); else if (Allowed(Access.Sale)) await Sales(); else Page("أهلًا " + login.DisplayName, "اختر القسم المطلوب من القوائم العلوية.");
        }));
        submit.HorizontalContentAlignment = HorizontalAlignment.Center;
        submit.Margin = new(0, 7, 0, 12);
        submit.IsDefault = true;
        var connection = new StackPanel { Margin = new(0, 14, 0, 0) };
        var address = Field(connection, "عنوان الخادم", api.Client.BaseAddress!.ToString(), double.NaN); address.FlowDirection = FlowDirection.LeftToRight;
        var reconnect = Action(connection, "اختيار قاعدة بيانات", async (_, _) => await Safe(async () =>
        {
            var selector = new DatabaseWindow(address.Text) { Owner = this };
            if (selector.ShowDialog() == true) { api = selector.SelectedApi!; Title = "موبي ستور — " + selector.SelectedName; await Connect(); }
        })); reconnect.Style = (Style)FindResource("SecondaryButton");
        form.Children.Add(new Expander { Header = "إعدادات الاتصال", Content = connection, FontSize = 11, Foreground = Color("#8B98AA"), Margin = new(0, 10, 0, 0) });
    }
    private sealed record SessionState(LoginResponse User, SetupStatus Company, DateTime ServerUtc);
    private void Navigation()
    {
        AuthSurface.Visibility = Visibility.Collapsed;
        Shell.Visibility = Visibility.Visible;
        ShopText.Text = company.ShopName;
        Nav.Children.Clear();
        SectionMenu.Items.Clear();
        var menus = new Dictionary<string, MenuItem>();
        void Link(string title, string glyph, Func<Task> page, bool show = true)
        {
            if (!show) return;
            var group = title switch
            {
                "المبيعات والكاشير" or "الفواتير والمرتجعات" => "الفواتير",
                "المواد والمستودعات" or "العملاء والحسابات" or "المستخدمون والصلاحيات" => "البطاقات",
                "المشتريات" => "المشتريات",
                "الموردون" => "الحسابات والصناديق",
                "الجرد والتحويل" or "إعلان المواد الجديدة" => "المواد",
                "الصيانة وسجل العمليات" => "الأدوات",
                "تحصيل العملاء" or "الصناديق والإغلاق" => "الحسابات والصناديق",
                "الموظفون والدوام" => "الموارد البشرية",
                _ => "ملف"
            };
            if (!menus.TryGetValue(group, out var menu))
            { menu = new MenuItem { Header = group, Padding = new(15, 8, 15, 8) }; menus[group] = menu; SectionMenu.Items.Add(menu); }
            var entry = new MenuItem { Header = title, Padding = new(15, 9, 15, 9) };
            entry.Click += async (_, _) => await Safe(page); menu.Items.Add(entry);
            var b = new Button { Style = (Style)FindResource("ToolbarButton") };
            var content = new Grid { FlowDirection = FlowDirection.LeftToRight };
            content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(27) });
            content.Children.Add(new TextBlock { Text = title, FlowDirection = FlowDirection.RightToLeft, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            var symbol = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(symbol, 1); content.Children.Add(symbol); b.Content = content;
            b.Click += async (_, _) => await Safe(async () => { foreach (var item in Nav.Children.OfType<Button>()) item.Tag = null; b.Tag = "active"; await page(); });
            if (Nav.Children.Count == 0) b.Tag = "active";
            Nav.Children.Add(b);
        }
        Link("نظرة عامة", "\uE80F", Dashboard, Allowed(Access.Accounts));
        Link("المبيعات والكاشير", "\uE719", Sales, Allowed(Access.Sale));
        Link("المواد والمستودعات", "\uE7B8", Products, Allowed(Access.Catalog));
        Link("المشتريات", "\uE8CC", Purchases, Allowed(Access.Purchase));
        Link("الموردون", "\uE8D4", Suppliers, Allowed(Access.Accounts));
        Link("الجرد والتحويل", "\uE8AB", Inventory, Allowed(Access.Catalog));
        Link("العملاء والحسابات", "\uE716", Customers, Allowed(Access.Customers) || Allowed(Access.Accounts));
        Link("الفواتير والمرتجعات", "\uE8A5", Invoices);
        Link("تحصيل العملاء", "\uE8D4", Receipts, login!.IsAdmin);
        Link("الصناديق والإغلاق", "\uE7C1", CashBoxes);
        Link("الموظفون والدوام", "\uE77B", Employees, Allowed(Access.Hr));
        Link("المستخدمون والصلاحيات", "\uE72E", Users, login.IsAdmin);
        Link("الصيانة وسجل العمليات", "\uE713", Maintenance, login.IsAdmin);
        Link("إعلان المواد الجديدة", "\uE8F2", Announcements, login.IsAdmin);
        Link("تسجيل الخروج", "\uE8AC", async () => { await api.Post<JsonElement>("api/logout", new { }); login = null; api.Client.DefaultRequestHeaders.Authorization = null; IdentityText.Text = "تسجيل الدخول"; await Connect(); });
    }
    private async Task LoadCatalog()
    {
        products = await api.Get<List<ProductView>>("api/products"); customers = await api.Get<List<CustomerView>>("api/customers"); warehouses = await api.Get<List<WarehouseView>>("api/warehouses");
    }
    private async Task Dashboard()
    {
        var d = await api.Get<DashboardView>("api/dashboard");
        Page("نظرة عامة", "صورة واضحة لحركة محلك، لحظة بلحظة. جميع المؤشرات بالعملة الأساسية " + d.BaseCurrency);
        var welcome = new Grid { FlowDirection = FlowDirection.LeftToRight };
        welcome.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); welcome.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var intro = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        intro.Children.Add(new TextBlock { Text = "أهلًا " + login!.DisplayName + "، يوم عمل موفق", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Color("#156E60") });
        intro.Children.Add(new TextBlock { Text = "تابع مبيعاتك، راجع الاستحقاقات، وابقَ على اطلاع بصناديق فريقك.", Foreground = Color("#5D8E84"), FontSize = 12, Margin = new(0, 9, 0, 0), TextWrapping = TextWrapping.Wrap }); Grid.SetColumn(intro, 0); welcome.Children.Add(intro);
        var quick = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(20, 0, 0, 0) }; Grid.SetColumn(quick, 1);
        if (Allowed(Access.Sale)) Action(quick, "+  فاتورة جديدة", async (_, _) => await Safe(Sales));
        welcome.Children.Add(quick);
        Body.Children.Add(new Border { Background = Color("#E9F6F1"), CornerRadius = new(13), Padding = new(24), Margin = new(0, 0, 0, 22), Child = welcome });
        var cards = Row();
        foreach (var (label, value, icon, color, tint) in new[] {
            ("مبيعات اليوم", d.SalesToday.ToString("N2"), "\uE719", "#078B78", "#E8F7F1"),
            ("أرصدة العملاء", d.CustomerDebt.ToString("N2"), "\uE716", "#5076CD", "#EDF2FD"),
            ("الصندوق الرئيسي", d.CashBase.ToString("N2"), "\uE7C1", "#AA7C26", "#FCF5E7"),
            ("صناديق مفتوحة", d.OpenSessions.ToString(), "\uE787", "#7B65BC", "#F2EFFA"),
            ("فواتير مستحقة", d.DueInvoices.ToString(), "\uE823", "#CD6D74", "#FCEFF0") })
        {
            var stack = new StackPanel(); var top = new DockPanel();
            var badge = new Border { Background = Color(tint), CornerRadius = new(8), Padding = new(9), Child = new TextBlock { Text = icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17, Foreground = Color(color) } };
            DockPanel.SetDock(badge, Dock.Left); top.Children.Add(badge); top.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Color("#7C8A9F"), VerticalAlignment = VerticalAlignment.Center }); stack.Children.Add(top);
            stack.Children.Add(new TextBlock { Text = value, FontSize = 29, FontWeight = FontWeights.SemiBold, Margin = new(0, 15, 0, 0), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right });
            cards.Children.Add(new Border { Child = stack, Background = Brushes.White, BorderBrush = Color("#E5EBF2"), BorderThickness = new(1), Padding = new(18), Margin = new(0, 0, 12, 12), Width = 184, CornerRadius = new(12) });
        }
        Body.Children.Add(new TextBlock { Text = "الاستحقاقات القادمة والمتأخرة", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 20, 0, 0) });
        Table(Body, await api.Get<List<DueView>>("api/due"), ("العميل", "Customer"), ("المتبقي", "Remaining"), ("الاستحقاق", "DueDate"), ("متأخرة", "Overdue"));
        Action(Body, "تحديث", async (_, _) => await Safe(Dashboard));
    }
    private async Task Products()
    {
        await LoadCatalog(); Page("المواد والمستودعات", "أسعار القطعة بالعملة الأساسية. الرصيد ناتج عن الشراء والبيع والمرتجعات.");
        var grid = Table(Body, products, ("الكود", "Code"), ("المادة", "Name"), ("الرصيد الإجمالي", "Stock"), ("مفرق", "RetailPrice"), ("جملة", "WholesalePrice"), ("التكلفة", "Cost"));
        Action(Body, "تعديل المادة والصورة والتوافق", async (_, _) => await Safe(async () =>
        { Rules.Require(grid.SelectedItem is ProductView, "اختر المادة من الجدول"); await ProductCard((ProductView)grid.SelectedItem); }));
        var row = Row(); var code = Field(row, "الكود", width: 130); var name = Field(row, "المادة", width: 250); var barcode = Field(row, "الباركود", width: 180);
        var row2 = Row(); var retail = Field(row2, "سعر المفرق", "0"); var wholesale = Field(row2, "سعر الجملة", "0"); var unit = Field(row2, "الوحدة الأساسية", "قطعة");
        Action(Body, "إضافة المادة", async (_, _) => await Safe(async () => { await api.Post<JsonElement>("api/products", new ProductInput(code.Text, name.Text, barcode.Text, unit.Text, 1, Money(retail), Money(wholesale))); await Products(); }));
        Note("وحدة إضافية للمادة المحددة في الجدول، مثال: علبة = 12 قطعة");
        var extra = Row(); var unitName = Field(extra, "اسم الوحدة", "علبة"); var factor = Field(extra, "عدد القطع", "12"); var unitBarcode = Field(extra, "باركود الوحدة");
        Action(Body, "إضافة وحدة للمادة المحددة", async (_, _) => await Safe(async () =>
        {
            Rules.Require(grid.SelectedItem is ProductView, "اختر مادة من الجدول"); var p = (ProductView)grid.SelectedItem;
            await api.Post<JsonElement>($"api/products/{p.Id}/units", new UnitView(Guid.Empty, unitName.Text, unitBarcode.Text, Money(factor))); await Products();
        }));
        var wh = Row(); var whName = Field(wh, "مستودع جديد");
        Action(wh, "إضافة مستودع", async (_, _) => await Safe(async () => { await api.Post<JsonElement>("api/warehouses", new WarehouseView(Guid.Empty, whName.Text)); await Products(); }));
        Note("المستودعات: " + string.Join(" • ", warehouses.Select(x => x.Name)));
    }
    private async Task Customers()
    {
        await LoadCatalog(); Page("العملاء والحسابات", "الديون وحد المديونية بالعملة الأساسية. تسوية الدفعات على أقدم الفواتير أولًا.");
        var grid = Table(Body, customers, ("الاسم", "Name"), ("الهاتف", "Phone"), ("الرصيد", "Balance"), ("حد المديونية", "CreditLimit"), ("أيام الآجل", "DueDays"), ("جملة", "Wholesale"));
        if (Allowed(Access.Catalog)) Action(Body, "أسعار خاصة للعميل المحدد", async (_, _) => await Safe(async () =>
        { Rules.Require(grid.SelectedItem is CustomerView, "اختر العميل"); await CustomerPrices((CustomerView)grid.SelectedItem); }));
        if (Allowed(Access.Accounts)) Action(Body, "كشف حساب العميل المحدد", async (_, _) => await Safe(async () =>
        {
            Rules.Require(grid.SelectedItem is CustomerView, "اختر العميل"); var c = (CustomerView)grid.SelectedItem;
            var entries = await api.Get<List<LedgerView>>($"api/customers/{c.Id}/ledger"); Page("كشف حساب — " + c.Name, "العملة الأساسية: " + company.BaseCurrency);
            Table(Body, entries, ("التاريخ", "CreatedUtc"), ("الحركة", "Type"), ("مدين", "Debit"), ("دائن", "Credit"), ("الرصيد", "Balance"));
        }));
        if (!Allowed(Access.Customers)) return;
        var row = Row(); var name = Field(row, "اسم العميل"); var phone = Field(row, "رقم الهاتف"); var limit = Field(row, "حد المديونية", "0"); var days = Field(row, "أيام الاستحقاق", "30", 120);
        var wholesale = new CheckBox { Content = "عميل جملة" }; Body.Children.Add(wholesale);
        Action(Body, "إضافة العميل", async (_, _) => await Safe(async () => { await api.Post<JsonElement>("api/customers", new CustomerInput(name.Text, phone.Text, Money(limit), int.Parse(days.Text), wholesale.IsChecked == true)); await Customers(); }));
    }
    private record HeldSale(string Key, string Json, DateTime UpdatedUtc) { public string Label => "فاتورة معلقة — " + UpdatedUtc.ToLocalTime().ToString("g"); }
    private record UnitOption(ProductView Product, UnitView Unit) { public string Label => $"{Product.Code} — {Product.Name} / {Unit.Name}"; }
    private record CartItem(Guid UnitId, string Name, string Unit, decimal Quantity, decimal Price, decimal Discount)
    { public decimal Total => Rules.LineTotal(Quantity, Price, Discount); }
    private SaleRequest? restoredSale;
    private async Task Sales()
    {
        await LoadCatalog(); var sessions = await api.Get<List<SessionView>>("api/sessions");
        Page("فاتورة مبيعات", "آخر سعر يظهر للمادة والعميل كمرجع. الحفظ يسجل حركة المخزون والقيد ودفعة الفاتورة معًا.");
        var mine = sessions.FirstOrDefault(x => x.UserId == login!.UserId);
        // Ownership is ultimately enforced on the server; resolve by the user's id through the open-session response.
        if (mine is null)
        {
            Note("افتح وردية لصندوقك قبل تسجيل المبيعات.");
            var request = Guid.NewGuid(); Action(Body, "فتح وردية", async (_, _) => await Safe(async () => { await api.Post<OperationResult>("api/sessions/open", new OpenSessionRequest(request)); await Sales(); })); return;
        }
        var drafts = await api.Get<JsonElement[]>("api/drafts");
        var held = Choice(Row(), "فواتير معلقة", drafts.Select(x => new HeldSale(x.GetProperty("key").GetString()!, x.GetProperty("json").GetString()!, x.GetProperty("updatedUtc").GetDateTime())).ToList(), "Label", 420);
        Action(Body, "استرجاع الفاتورة المعلقة", async (_, _) => await Safe(async () => { restoredSale = JsonSerializer.Deserialize<SaleRequest>(Selected<HeldSale>(held).Json); await Sales(); }));
        var savedRate = (await api.Get<RateInput>("api/exchange-rate")).Rate.ToString(CultureInfo.InvariantCulture);
        var head = Row(); var customer = Choice(head, "العميل", customers, "Name", 250); var warehouse = Choice(head, "المستودع", warehouses, "Name"); var currency = Choice(head, "عملة الفاتورة", new[] { "USD", "SYP" }, width: 130); currency.SelectedItem = company.BaseCurrency;
        var fx = Row(); var rate = Field(fx, "ليرة لكل دولار", savedRate); var paymentCurrency = Choice(fx, "عملة الدفعة", new[] { "USD", "SYP" }, width: 130); paymentCurrency.SelectedItem = company.BaseCurrency;
        var paid = Field(fx, "الدفعة النقدية", "0", 160); var paymentRate = Field(fx, "سعر صرف الدفعة", savedRate, 160); var due = Field(fx, "الاستحقاق yyyy-MM-dd", DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"), 180);
        var row = Row(); var options = products.SelectMany(p => p.Units.Select(u => new UnitOption(p, u))).ToList(); var material = Choice(row, "المادة / الوحدة", options, "Label", 350);
        var qty = Field(row, "الكمية", "1", 110); var price = Field(row, "سعر الوحدة", "0", 130); var discount = Field(row, "خصم السطر", "0", 130);
        var last = new TextBlock { Text = "اختر مادة لعرض آخر سعر بيع لنفس العميل", Foreground = Brushes.Teal, TextWrapping = TextWrapping.Wrap }; Body.Children.Add(last);
        async Task Price()
        {
            if (material.SelectedItem is not UnitOption choice || customer.SelectedItem is not CustomerView c) return;
            var basePrice = (await api.Get<PriceInput>($"api/prices/{c.Id}/{choice.Product.Id}")).Price * choice.Unit.Factor;
            var r = Money(rate); Rules.Require(r > 0, "أدخل سعر الصرف");
            price.Text = Rules.Round(Selected<string>(currency) == company.BaseCurrency ? basePrice : company.BaseCurrency == "USD" ? basePrice * r : basePrice / r).ToString(CultureInfo.InvariantCulture);
            var previous = await api.Get<LastPriceView?>($"api/customers/{c.Id}/last-price/{choice.Product.Id}");
            last.Text = previous is null ? "لا يوجد بيع سابق لهذه المادة إلى العميل" : $"آخر بيع: {previous.Price:N4} {previous.Currency} / {previous.Unit} • خصم السطر {previous.Discount:N4} لكمية {previous.Quantity} • {previous.DateUtc.ToLocalTime():yyyy-MM-dd}";
        }
        material.SelectionChanged += async (_, _) => await Safe(Price); customer.SelectionChanged += async (_, _) => await Safe(Price); currency.SelectionChanged += async (_, _) => await Safe(Price);
        var cart = new ObservableCollection<CartItem>(); var grid = Table(Body, cart, ("المادة", "Name"), ("الوحدة", "Unit"), ("الكمية", "Quantity"), ("السعر", "Price"), ("الخصم", "Discount"), ("المجموع", "Total"));
        var total = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Text = "الإجمالي: 0" }; Body.Children.Add(total);
        var actions = Row();
        void AddLine()
        {
            var u = Selected<UnitOption>(material); var item = new CartItem(u.Unit.Id, u.Product.Name, u.Unit.Name, Money(qty), Money(price), Money(discount)); _ = item.Total;
            var existing = cart.FirstOrDefault(x => x.UnitId == item.UnitId);
            if (existing is not null)
            {
                Rules.Require(existing.Price == item.Price, "سعر السطر المكرر مختلف؛ عدّل السطر أولًا");
                cart.Remove(existing); item = item with { Quantity = existing.Quantity + item.Quantity, Discount = existing.Discount + item.Discount };
            }
            cart.Add(item); total.Text = $"الإجمالي: {cart.Sum(x => x.Total):N4} {currency.SelectedItem}";
        }
        Action(actions, "إضافة للسلة", (_, _) => { try { AddLine(); } catch (Exception ex) when (ex is RuleException or FormatException) { StatusText.Text = ex.Message; } });
        var scan = Field(Row(), "مسح الباركود ثم Enter", "", 420);
        scan.FlowDirection = FlowDirection.LeftToRight;
        scan.KeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return; e.Handled = true;
            await Safe(async () =>
            {
                var matches = options.Where(x => x.Unit.Barcode != "" && x.Unit.Barcode == scan.Text.Trim()).ToList();
                Rules.Require(matches.Count == 1, "الباركود غير موجود أو غير محدد"); material.SelectedItem = matches[0];
                qty.Text = "1"; discount.Text = "0"; await Price(); AddLine(); scan.Clear(); scan.Focus();
            });
        };
        Action(actions, "حذف السطر المحدد", (_, _) => { if (grid.SelectedItem is CartItem item) cart.Remove(item); total.Text = $"الإجمالي: {cart.Sum(x => x.Total):N4}"; });
        var requestId = restoredSale?.RequestId ?? Guid.NewGuid(); SaleRequest? pending = null;
        SaleRequest Snapshot() => new(requestId, mine.Id, Selected<WarehouseView>(warehouse).Id, Selected<CustomerView>(customer).Id,
            Selected<string>(currency), Money(rate), Money(paid), Selected<string>(paymentCurrency), Money(paymentRate), DateOnly.ParseExact(due.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture), cart.Select(x => new SaleLineInput(x.UnitId, x.Quantity, x.Price, x.Discount)).ToArray());
        if (restoredSale is { } draft)
        {
            customer.SelectedItem = customers.FirstOrDefault(x => x.Id == draft.CustomerId); warehouse.SelectedItem = warehouses.FirstOrDefault(x => x.Id == draft.WarehouseId);
            currency.SelectedItem = draft.Currency; paymentCurrency.SelectedItem = draft.PaymentCurrency;
            rate.Text = draft.Rate.ToString(CultureInfo.InvariantCulture); paymentRate.Text = draft.PaymentRate.ToString(CultureInfo.InvariantCulture); paid.Text = draft.CashPaid.ToString(CultureInfo.InvariantCulture);
            due.Text = (draft.DueDate ?? DateOnly.FromDateTime(DateTime.Today)).ToString("yyyy-MM-dd");
            foreach (var line in draft.Lines)
            {
                var option = options.SingleOrDefault(x => x.Unit.Id == line.UnitId); Rules.Require(option is not null, "إحدى مواد الفاتورة المعلقة غير متاحة");
                cart.Add(new(line.UnitId, option!.Product.Name, option.Unit.Name, line.Quantity, line.UnitPrice, line.Discount));
            }
            total.Text = $"الإجمالي: {cart.Sum(x => x.Total):N4} {currency.SelectedItem}"; restoredSale = null;
        }
        var holdButton = Action(actions, "تعليق الفاتورة", async (_, _) => await Safe(async () => { Rules.Require(cart.Count > 0, "أدخل مادة أولًا"); await api.Post<JsonElement>($"api/drafts/{requestId}", Snapshot()); await Sales(); }));
        var saveButton = Action(actions, "حفظ وترحيل الفاتورة", async (_, _) => await Safe(async () =>
        {
            pending ??= Snapshot();
            try { var result = await api.Post<OperationResult>("api/sales", pending); MessageBox.Show("تم حفظ الفاتورة\n" + result.Number, "المبيعات"); await api.Delete($"api/drafts/{requestId}"); await Sales(); }
            catch (ApiException) { pending = null; throw; }
        }));
        InputBindings.Add(new System.Windows.Input.KeyBinding(new UiCommand(() => scan.Focus()), System.Windows.Input.Key.F2, System.Windows.Input.ModifierKeys.None));
        InputBindings.Add(new System.Windows.Input.KeyBinding(new UiCommand(() => holdButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))), System.Windows.Input.Key.F8, System.Windows.Input.ModifierKeys.None));
        InputBindings.Add(new System.Windows.Input.KeyBinding(new UiCommand(() => saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))), System.Windows.Input.Key.F9, System.Windows.Input.ModifierKeys.None));
        Note("اختصارات البيع: F2 للباركود، F8 لتعليق الفاتورة، F9 للحفظ والترحيل.");
        await Price();
        Note("عند انقطاع الاتصال أعد ضغط الحفظ بنفس الشاشة؛ يحتفظ الطلب بمعرفه لمنع التكرار. لتغيير سعر الصرف حدّث اختيار المادة قبل إضافتها.");
    }
    private async Task Purchases()
    {
        await LoadCatalog(); Page("إدخال مشتريات", "تزيد الكمية ويُحسب متوسط التكلفة بالعملة الأساسية. لكل مورد كشف مستقل للمشتريات والمرتجعات والدفعات.");
        if (Allowed(Access.Return)) Action(Body, "مرتجع من فاتورة شراء سابقة", async (_, _) => await Safe(PurchaseReturns));
        var row = Row(); var supplier = Field(row, "المورد", ""); var warehouse = Choice(row, "المستودع", warehouses, "Name"); var currency = Choice(row, "العملة", new[] { "USD", "SYP" }, width: 130); var rate = Field(row, "ليرة لكل دولار", "1", 150);
        var items = Row(); var unit = Choice(items, "المادة / الوحدة", products.SelectMany(p => p.Units.Select(u => new UnitOption(p, u))).ToList(), "Label", 350); var qty = Field(items, "الكمية", "1", 130); var cost = Field(items, "تكلفة الوحدة", "0", 150);
        var cart = new ObservableCollection<CartItem>(); Table(Body, cart, ("المادة", "Name"), ("الوحدة", "Unit"), ("الكمية", "Quantity"), ("التكلفة", "Price"), ("المجموع", "Total"));
        Action(Body, "إضافة بند", (_, _) => { try { var u = Selected<UnitOption>(unit); var item = new CartItem(u.Unit.Id, u.Product.Name, u.Unit.Name, Money(qty), Money(cost), 0); Rules.Require(item.Total > 0, "أدخل الكمية والتكلفة"); cart.Add(item); } catch (Exception e) when (e is RuleException or FormatException) { StatusText.Text = e.Message; } });
        var id = Guid.NewGuid(); PurchaseRequest? pending = null;
        Action(Body, "حفظ المشتريات وإدخال المخزون", async (_, _) => await Safe(async () =>
        {
            pending ??= new(id, Selected<WarehouseView>(warehouse).Id, supplier.Text, Selected<string>(currency), Money(rate), cart.Select(x => new PurchaseLineInput(x.UnitId, x.Quantity, x.Price)).ToArray());
            try { await api.Post<OperationResult>("api/purchases", pending); await Purchases(); } catch (ApiException) { pending = null; throw; }
        }));
    }
    private async Task Receipts()
    {
        await LoadCatalog(); var sessions = await api.Get<List<SessionView>>("api/sessions"); var mine = sessions.FirstOrDefault(x => x.UserId == login!.UserId);
        Page("تحصيل دفعة من عميل", "للـمدير فقط • القبض على صندوق المدير، ويُنقل للصندوق الرئيسي عند الإغلاق اليومي.");
        if (mine is null) { var open = Guid.NewGuid(); Action(Body, "فتح وردية صندوق المدير", async (_, _) => await Safe(async () => { await api.Post<OperationResult>("api/sessions/open", new OpenSessionRequest(open)); await Receipts(); })); return; }
        var row = Row(); var customer = Choice(row, "العميل", customers.Where(x => x.Balance > 0).ToList(), "Name", 300); var amount = Field(row, "المبلغ", "0"); var currency = Choice(row, "العملة", new[] { "USD", "SYP" }, width: 130); var rate = Field(row, "ليرة لكل دولار", "1", 150);
        var notes = Field(Body, "البيان", "دفعة على الحساب", 500); var id = Guid.NewGuid(); ReceiptRequest? pending = null;
        Action(Body, "تسجيل القبض وإصدار الإيصال", async (_, _) => await Safe(async () =>
        {
            pending ??= new(id, mine.Id, Selected<CustomerView>(customer).Id, Selected<string>(currency), Money(amount), Money(rate), notes.Text);
            OperationResult result;
            try { result = await api.Post<OperationResult>("api/receipts", pending); } catch (ApiException) { pending = null; throw; }
            var receipt = await api.Get<JsonElement>($"api/receipts/{result.Id}");
            // The database has already committed. PDF cancellation must never repeat the payment.
            await Receipts();
            var save = new Microsoft.Win32.SaveFileDialog { Filter = "PDF document|*.pdf", FileName = result.Number + ".pdf" };
            if (save.ShowDialog(this) == true) ReceiptPdf.Save(save.FileName, company.ShopName, receipt);
            MessageBox.Show("تم تسجيل القبض على صندوق المدير", "التحصيل");
        }));
        Table(Body, customers.Where(x => x.Balance > 0), ("العميل", "Name"), ("الدين بالعملة الأساسية", "Balance"));
    }
    private async Task CashBoxes()
    {
        var sessions = await api.Get<List<SessionView>>("api/sessions"); Page("الصناديق والإغلاق اليومي", "إغلاق كل صندوق ينقل النقد بالدولار والليرة إلى الصندوق الرئيسي، دون تكرار المبيعات أو التحصيل.");
        Table(Body, sessions.Select(x => new { x.Owner, x.OpenedUtc, USD = x.Expected.First(c => c.Currency == "USD").Amount, SYP = x.Expected.First(c => c.Currency == "SYP").Amount }), ("المستخدم", "Owner"), ("افتتاح الوردية", "OpenedUtc"), ("دولار متوقع", "USD"), ("ليرة متوقعة", "SYP"));
        if (!login!.IsAdmin) return;
        var row = Row(); var chosen = Choice(row, "الصندوق", sessions, "Owner", 260); var usd = Field(row, "الدولار المسلّم فعليًا", "0"); var syp = Field(row, "الليرات المسلّمة فعليًا", "0");
        var id = Guid.NewGuid(); CloseRequest? pending = null;
        Action(Body, "اعتماد التسليم وإغلاق الصندوق", async (_, _) => await Safe(async () =>
        {
            if (pending is null)
            {
                if (MessageBox.Show("هل تؤكد استلام النقد المسجل ونقله للصندوق الرئيسي وإغلاق الوردية؟", "اعتماد إغلاق الصندوق", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                pending = new(id, Selected<SessionView>(chosen).Id, [new("USD", Money(usd)), new("SYP", Money(syp))]);
            }
            try { await api.Post<OperationResult>("api/sessions/close", pending); await CashBoxes(); } catch (ApiException) { pending = null; throw; }
        }));
        Note("في هذا الإصدار يجب تطابق الجرد مع المتوقع. تسجيل العجز والفائض واعتماده سيضاف بدورة مستقلة.");
    }
    private async Task Invoices()
    {
        var rows = await api.Get<List<InvoiceView>>("api/invoices"); Page("الفواتير والمرتجعات", "الفاتورة المرحلة محفوظة. المرتجع يصدر كعملية مستقلة ويُعاد المخزون ويُقيّد رصيد للعميل.");
        var grid = Table(Body, rows, ("التاريخ", "CreatedUtc"), ("العميل", "Customer"), ("البائع", "Seller"), ("العملة", "Currency"), ("الإجمالي", "Total"), ("المسدد بالأساسية", "PaidBase"));
        Action(Body, "عرض تفاصيل الفاتورة", async (_, _) => await Safe(async () =>
        {
            Rules.Require(grid.SelectedItem is InvoiceView, "اختر فاتورة"); var invoice = (InvoiceView)grid.SelectedItem;
            var detail = await api.Get<JsonElement>($"api/invoices/{invoice.Id}"); Page("تفاصيل الفاتورة", invoice.Number);
            var lines = detail.GetProperty("lines").EnumerateArray().Select(x => new ReturnLineView(x.GetProperty("id").GetGuid(), x.GetProperty("productName").GetString()!, x.GetProperty("unitName").GetString()!, x.GetProperty("quantity").GetDecimal(), x.GetProperty("returned").GetDecimal(), x.GetProperty("price").GetDecimal())).ToList();
            var lineGrid = Table(Body, lines, ("المادة", "Name"), ("الوحدة", "Unit"), ("الكمية", "Quantity"), ("مرتجع سابق", "Returned"), ("سعر البيع", "Price"));
            if (!Allowed(Access.Return)) return;
            var qty = Field(Body, "كمية إرجاع السطر المحدد", "1"); var id = Guid.NewGuid(); ReturnRequest? pending = null;
            Action(Body, "تسجيل مرتجع كرصيد للعميل", async (_, _) => await Safe(async () =>
            {
                Rules.Require(lineGrid.SelectedItem is ReturnLineView, "اختر السطر"); var line = (ReturnLineView)lineGrid.SelectedItem;
                pending ??= new(id, invoice.Id, [new(line.Id, Money(qty))]);
                try { await api.Post<OperationResult>("api/returns", pending); await Invoices(); } catch (ApiException) { pending = null; throw; }
            }));
            Note("المرتجع ينشئ رصيدًا دائنًا للعميل؛ صرف رد نقدي غير متاح في هذه المرحلة.");
        }));
    }
    private record ReturnLineView(Guid Id, string Name, string Unit, decimal Quantity, decimal Returned, decimal Price);
    private async Task Users()
    {
        var users = await api.Get<List<UserView>>("api/users"); Page("المستخدمون والصلاحيات", "لكل موظف حساب مستقل وصندوق وردية. الصلاحيات تُفحص داخل الخادم أيضًا.");
        var selectedUser = Table(Body, users, ("اسم الدخول", "Username"), ("الاسم", "DisplayName"), ("مدير", "IsAdmin"), ("فعال", "Active"));
        Action(Body, "تعديل صلاحيات المستخدم المحدد", async (_, _) => await Safe(async () => { Rules.Require(selectedUser.SelectedItem is UserView, "اختر مستخدمًا"); await EditUser((UserView)selectedUser.SelectedItem); }));
        var row = Row(); var name = Field(row, "اسم الموظف"); var username = Field(row, "اسم الدخول"); var maxDisc = Field(row, "أقصى خصم %", "0", 150);
        Body.Children.Add(new TextBlock { Text = "كلمة مرور أولية (12 حرفًا على الأقل)" }); var pass = new PasswordBox { Width = 350, HorizontalAlignment = HorizontalAlignment.Right }; Body.Children.Add(pass);
        var admin = new CheckBox { Content = "مدير — جميع الصلاحيات بما فيها تحصيل العملاء" }; Body.Children.Add(admin);
        var labels = new Dictionary<string, string> { [Access.Catalog] = "إدارة المواد والمستودعات", [Access.Customers] = "إضافة العملاء", [Access.Purchase] = "تسجيل المشتريات", [Access.Sale] = "البيع", [Access.Discount] = "الخصم", [Access.Credit] = "البيع الآجل", [Access.Return] = "المرتجعات", [Access.Costs] = "إظهار التكلفة", [Access.Accounts] = "الحسابات والأرصدة", [Access.Close] = "إغلاق الصندوق (للمدير حاليًا)", [Access.Users] = "إدارة المستخدمين (للمدير حاليًا)", [Access.Hr] = "الموارد البشرية", [Access.Publish] = "النشر (مرحلة لاحقة)", [Access.Collect] = "التحصيل (للمدير فقط)" };
        var checks = new Dictionary<string, CheckBox>(); var rights = Row();
        foreach (var p in Access.All) { var box = new CheckBox { Content = labels[p], IsChecked = p == Access.Sale, Width = 245 }; rights.Children.Add(box); checks[p] = box; }
        Action(Body, "إنشاء المستخدم", async (_, _) => await Safe(async () =>
        {
            await api.Post<JsonElement>("api/users", new UserInput(username.Text, name.Text, pass.Password, admin.IsChecked == true, checks.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray(), Money(maxDisc))); pass.Clear(); await Users();
        }));
    }
    private async Task Employees()
    {
        var employees = await api.Get<List<EmployeeView>>("api/employees"); Page("الموظفون والدوام", "راتب ثابت وهدف مبيعات ومكافأة مقترحة عند تحقيقه. الصرف الفعلي للرواتب غير متاح بعد.");
        var grid = Table(Body, employees, ("الموظف", "Name"), ("الراتب", "MonthlySalary"), ("هدف المبيعات", "SalesTarget"), ("مكافأة الهدف", "TargetBonus"));
        var row = Row(); var salary = Field(row, "الراتب الثابت", "0"); var target = Field(row, "هدف المبيعات الشهري", "0"); var bonus = Field(row, "مكافأة تحقيق الهدف", "0");
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is EmployeeView e) { salary.Text = e.MonthlySalary.ToString(CultureInfo.InvariantCulture); target.Text = e.SalesTarget.ToString(CultureInfo.InvariantCulture); bonus.Text = e.TargetBonus.ToString(CultureInfo.InvariantCulture); } };
        Action(Body, "حفظ راتب وهدف الموظف المحدد", async (_, _) => await Safe(async () => { Rules.Require(grid.SelectedItem is EmployeeView, "اختر الموظف"); var e = (EmployeeView)grid.SelectedItem; await api.Put($"api/employees/{e.Id}", new EmployeeInput(e.Name, Money(salary), Money(target), Money(bonus))); await Employees(); }));
        var attendanceRow = Row(); var date = Field(attendanceRow, "تاريخ الدوام", DateTime.Today.ToString("yyyy-MM-dd")); var present = new CheckBox { Content = "حاضر", IsChecked = true }; attendanceRow.Children.Add(present);
        Action(attendanceRow, "تسجيل الدوام للموظف المحدد", async (_, _) => await Safe(async () => { Rules.Require(grid.SelectedItem is EmployeeView, "اختر الموظف"); await api.Post<JsonElement>("api/attendance", new AttendanceInput(((EmployeeView)grid.SelectedItem).Id, DateOnly.ParseExact(date.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture), present.IsChecked == true, "")); }));
        Action(Body, "معاينة استحقاقات الشهر الحالي", async (_, _) => await Safe(async () => { var now = DateTime.Now; var rows = await api.Get<List<PayrollView>>($"api/payroll?year={now.Year}&month={now.Month}"); Page("معاينة الرواتب والمكافآت", "مقترح للمراجعة؛ لا ينشئ سند صرف. صافي مبيعات فواتير الشهر بعد المرتجعات المسجلة عليها."); Table(Body, rows, ("الموظف", "Employee"), ("الراتب", "Salary"), ("صافي المبيعات", "NetSales"), ("الهدف", "Target"), ("المكافأة المقترحة", "SuggestedBonus"), ("أيام الحضور", "AttendanceDays")); }));
    }
}
