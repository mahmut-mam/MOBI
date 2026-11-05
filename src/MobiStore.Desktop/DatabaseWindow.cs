using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MobiStore.Desktop;

public record DatabaseProfile(string Name, string Title);
public partial class DatabaseWindow : Window
{
    private bool busy;
    public Api? SelectedApi { get; private set; }
    public string SelectedName { get; private set; } = "";
    private static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MobiStore", "database-selection.json");
    private record Selection(string Address, string Name, bool Automatic);
    public DatabaseWindow(string server)
    {
        InitializeComponent();
        address.Text = server;
        databases.SelectionChanged += (_, _) =>
        {
            details.Text = databases.SelectedItem is DatabaseProfile p ? p.Name : "اختر قاعدة للمتابعة";
            openButton.IsEnabled = databases.SelectedItem is DatabaseProfile;
        };
        openButton.Click += async (_, _) => await Run(Open);
        refreshButton.Click += async (_, _) => await Run(Refresh);
        newButton.Click += async (_, _) => await Run(() => Edit(true));
        registerButton.Click += async (_, _) => await Run(() => Edit(false));
        cancelButton.Click += (_, _) => DialogResult = false;
        Loaded += async (_, _) =>
        {
            Selection? saved = null;
            try { if (File.Exists(SettingsFile)) saved = JsonSerializer.Deserialize<Selection>(File.ReadAllText(SettingsFile)); } catch (Exception ex) when (ex is IOException or JsonException) { }
            if (saved is not null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MOBISTORE_SERVER"))) address.Text = saved.Address;
            automatic.IsChecked = saved?.Automatic == true;
            await Run(async () =>
            {
                await Refresh();
                if (saved is not null) databases.SelectedItem = databases.Items.Cast<DatabaseProfile>().FirstOrDefault(x => x.Name == saved.Name) ?? databases.SelectedItem;
                if (saved?.Automatic == true && databases.SelectedItem is DatabaseProfile selected && selected.Name == saved.Name) await Open();
            });
        };
    }
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => await Run(action); return button;
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; panel.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { details.Text = "تعذر إتمام العملية: " + ex.Message; }
        finally { busy = false; panel.IsEnabled = true; }
    }
    private Api Client()
    {
        if (!Uri.TryCreate(address.Text.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new IOException("أدخل عنوان خادم صحيحًا يبدأ بـ http أو https");
        return new Api(uri.ToString());
    }
    private async Task Refresh()
    {
        var rows = await Client().Get<DatabaseProfile[]>("api/databases");
        databases.ItemsSource = rows; databases.SelectedIndex = rows.Length > 0 ? 0 : -1;
        countText.Text = rows.Length + " قواعد مسجلة";
        connectionText.Text = "تم الاتصال بالخادم";
    }
    private async Task Open()
    {
        if (databases.SelectedItem is not DatabaseProfile selected) throw new IOException("اختر قاعدة بيانات");
        var api = Client(); api.Client.DefaultRequestHeaders.Add("X-MobiStore-Database", selected.Name);
        await api.Get<MobiStore.Core.SetupStatus>("api/setup");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Selection(address.Text.Trim(), selected.Name, automatic.IsChecked == true)));
        SelectedApi = api; SelectedName = selected.Name; DialogResult = true;
    }
    private Task Edit(bool create)
    {
        var form = new StackPanel { Margin = new(32), FlowDirection = FlowDirection.RightToLeft };
        var dialog = new Window { Title = create ? "إنشاء قاعدة بيانات" : "تسجيل قاعدة موبي ستور موجودة", Icon = (ImageSource)FindResource("BrandMark"), Background = Brushes.White, Width = 570, Height = 580, ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = form };
        form.Children.Add(new Image { Source = (ImageSource)FindResource("BrandMark"), Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 0, 20) });
        form.Children.Add(new TextBlock { Text = create ? "قاعدة جديدة على SQL Server" : "إضافة قاعدة موجودة على نفس خادم SQL", FontSize = 20, FontWeight = FontWeights.Bold });
        form.Children.Add(new TextBlock { Text = "الاسم الوصفي", Margin = new(0, 18, 0, 0) }); var title = new TextBox { Text = "السنة المالية " + DateTime.Now.Year }; form.Children.Add(title);
        form.Children.Add(new TextBlock { Text = "اسم قاعدة البيانات" }); var name = new TextBox { Text = "MobiStore_" + DateTime.Now.Year, FlowDirection = FlowDirection.LeftToRight }; form.Children.Add(name);
        var message = new TextBlock { Text = create ? "بعد الإنشاء ستفتح إعداد المنشأة لاختيار العملة وإنشاء حساب المدير." : "تقبل الإضافة قواعد موبي ستور المتوافقة فقط. أدخل اسمها كما يظهر في SQL Server.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 15) }; form.Children.Add(message);
        var save = new Button { Content = create ? "إنشاء" : "إضافة" }; form.Children.Add(save);
        save.HorizontalContentAlignment = HorizontalAlignment.Center;
        save.Click += async (_, _) =>
        {
            form.IsEnabled = false;
            try
            {
                var profile = await Client().Post<DatabaseProfile>(create ? "api/databases/create" : "api/databases/register", new DatabaseProfile(name.Text.Trim(), title.Text.Trim()));
                await Refresh(); databases.SelectedItem = databases.Items.Cast<DatabaseProfile>().First(x => x.Name == profile.Name);
                dialog.DialogResult = true;
            }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { form.IsEnabled = true; }
        };
        dialog.ShowDialog(); return Task.CompletedTask;
    }
}
