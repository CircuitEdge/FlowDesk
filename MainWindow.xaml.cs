using System.Diagnostics;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FlowDeck.Services;
using FlowDeck.ViewModels;

namespace FlowDeck;


public partial class MainWindow : Window
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSpace = 0x20;
    private readonly MainWindowViewModel _viewModel = new();
    private readonly AppDiscoveryService _appDiscoveryService = new();
    private readonly FlowDeckStorageService _storageService = new();
    private readonly WindowsStartupService _startupService = new();
    private readonly ClipboardPrivacyService _clipboardPrivacyService = new();
    private List<string> _notes => _viewModel.Notes;
    private List<string> _clipboardHistory => _viewModel.ClipboardHistory;
    private ObservableCollection<DiscoveredApp> _discoveredApps => _viewModel.DiscoveredApps;
    private Dictionary<string, int> _appLaunchCounts => _viewModel.AppLaunchCounts;
    private HwndSource? _windowSource;
    private bool _isAnimating;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _clipboardTicker = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _scrollAnimationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Dictionary<FrameworkElement, double> _fontSizeBaselines = [];
    private bool _selectingSuggestion;
    private bool _navigatingSuggestions;
    private bool _suggestionsShouldBeVisible;
    private ScrollViewer? _scrollAnimationViewer;
    private double _scrollAnimationTarget;
    private double _scrollAnimationVelocity;
    private long _scrollAnimationLastTimestamp;
    private bool _isRussian;
    private bool _languageInitialized;
    private string _historyView = "clipboard";
    private int _historyTransitionVersion;
    private static readonly FlowDeckCommand[] BuiltInCommands =
    [
        new("/timer", "Start a countdown timer"),
        new("/session", "Start an activity session"),
        new("/stop", "Stop the current timer"),
        new("/note", "Save a scratchpad note"),
        new("/notes", "View saved notes"),
        new("/search", "Search the web in your browser"),
        new("/close", "Gracefully close a named app"),
        new("/folder", "Open a folder by path"),
        new("/volume", "Get or set system volume (0-100, up, down)"),
        new("/mute", "Toggle system audio mute"),
        new("/translate", "Open Google Translate"),
        new("/ip", "Show local IP addresses"),
        new("/calc", "Calculate a math expression"),
        new("/clipboard", "View clipboard history"),
        new("/focus", "Toggle focus mode"),
        new("/apps", "List discovered apps"),
        new("/startup", "Enable, disable, or check Windows startup")
    ];
    private static readonly FlowDeckCommand[] SearchProviders =
    [
        new("/search google", "Search Google"),
        new("/search youtube", "Search YouTube"),
        new("/search twitch", "Search Twitch"),
        new("/search discord", "Search public Discord pages"),
        new("/search reddit", "Search Reddit"),
        new("/search github", "Search GitHub")
    ];
    private static readonly IReadOnlyDictionary<string, string> RussianText = new Dictionary<string, string>
    {
        ["Close"] = "Закрыть",
        ["APP DISCOVERY"] = "ПОИСК ПРИЛОЖЕНИЙ",
        ["Searching installed .exe files and Start Menu shortcuts."] = "Поиск установленных .exe-файлов и ярлыков меню «Пуск».",
        ["Scanning apps..."] = "Поиск приложений...",
        ["Scan again"] = "Сканировать снова",
        ["Quick Launch"] = "Быстрый запуск",
        ["Start typing an installed app and press Enter"] = "Начните вводить название приложения и нажмите Enter",
        ["Switch language"] = "Сменить язык",
        ["Show commands"] = "Показать команды",
        ["What do you want to do?"] = "Что вы хотите сделать?",
        ["Try typing a command or an app name"] = "Введите команду или название приложения",
        ["Ready. Your command results will appear here."] = "Готово. Здесь появятся результаты команды.",
        ["Session"] = "Сеанс",
        ["Stop"] = "Остановить",
        ["Scratchpad"] = "Заметки",
        ["View notes"] = "Открыть заметки",
        ["Clipboard"] = "Буфер обмена",
        ["View history"] = "Открыть историю",
        ["Clipboard history"] = "История буфера обмена",
        ["PRIVACY"] = "КОНФИДЕНЦИАЛЬНОСТЬ",
        ["Capture clipboard history"] = "Собирать историю буфера обмена",
        ["History is kept in memory only. Windows opt-out-marked content is ignored."] = "История хранится только в памяти. Содержимое с запретом Windows не сохраняется.",
        ["Excluded applications (process names)"] = "Исключённые приложения (имена процессов)",
        ["Separate names with commas or new lines; for example, password-manager.exe"] = "Разделяйте имена запятыми или переносами строк, например password-manager.exe",
        ["Save exclusions"] = "Сохранить исключения",
        ["Clear clipboard history"] = "Очистить историю буфера обмена",
        ["Clipboard exclusions saved."] = "Исключения буфера обмена сохранены.",
        ["Clipboard history cleared."] = "История буфера обмена очищена.",
        ["Clipboard capture enabled."] = "Сбор данных буфера обмена включён.",
        ["Clipboard capture disabled. Existing history is unchanged."] = "Сбор данных буфера обмена выключен. Текущая история не изменена.",
        ["Could not save clipboard privacy settings: {0}"] = "Не удалось сохранить настройки буфера обмена: {0}",
        ["Could not load clipboard privacy settings: {0}"] = "Не удалось загрузить настройки буфера обмена: {0}",
        ["Notes"] = "Заметки",
        ["No applications found yet."] = "Приложения пока не найдены.",
        ["FlowDeck is not configured to start with Windows."] = "FlowDeck не настроен для запуска вместе с Windows.",
        ["FlowDeck is configured to start with Windows."] = "FlowDeck настроен для запуска вместе с Windows.",
        ["FlowDeck will no longer start with Windows."] = "FlowDeck больше не будет запускаться вместе с Windows.",
        ["Use: /startup on, /startup off, or /startup status."] = "Используйте: /startup on, /startup off или /startup status.",
        ["Could not determine the FlowDeck executable path."] = "Не удалось определить путь к FlowDeck.",
        ["FlowDeck will start with Windows."] = "FlowDeck будет запускаться вместе с Windows.",
        ["Choose a site and enter a search term, for example: /search youtube music."] = "Выберите сайт и введите запрос, например: /search youtube музыка.",
        ["Opening public Discord web results in your browser. Private Discord content is not searchable this way."] = "Открываем общедоступные результаты Discord в браузере. Личные данные Discord так найти нельзя.",
        ["Enter a note after the note command."] = "Введите текст заметки после команды /note.",
        ["Note saved to your scratchpad."] = "Заметка сохранена.",
        ["No notes yet. Try: /note remember to fix flashlight"] = "Заметок пока нет. Попробуйте: /note не забыть починить фонарик",
        ["Scratchpad is empty."] = "Список заметок пуст.",
        ["Timer format: timer 30m, timer 1h, or timer 45s."] = "Формат таймера: /timer 30m, /timer 1h или /timer 45s.",
        ["Timer and activity session stopped."] = "Таймер и сеанс остановлены.",
        ["Timer finished."] = "Время вышло.",
        ["Copy some text and it will appear here."] = "Скопируйте текст, и он появится здесь.",
        ["Calculation format: calc 18 * 7"] = "Формат вычисления: /calc 18 * 7",
        ["Use only numbers and math operators."] = "Используйте только числа и математические операторы.",
        ["That calculation could not be evaluated."] = "Не удалось выполнить вычисление.",
        ["Focus mode off."] = "Режим фокусировки выключен.",
        ["Focus mode on. Intent: {0}"] = "Режим фокусировки включён. Цель: {0}",
        ["Stay on the current task"] = "Сосредоточиться на текущей задаче",
        ["Search the web in your browser"] = "Искать в интернете через браузер",
        ["Start a countdown timer"] = "Запустить таймер обратного отсчёта",
        ["Start an activity session"] = "Начать рабочий сеанс",
        ["Stop the current timer"] = "Остановить текущий таймер",
        ["Save a scratchpad note"] = "Сохранить заметку",
        ["View saved notes"] = "Показать сохранённые заметки",
        ["Calculate a math expression"] = "Вычислить математическое выражение",
        ["View clipboard history"] = "Показать историю буфера обмена",
        ["Toggle focus mode"] = "Включить или выключить режим фокусировки",
        ["Launch an installed app"] = "Запустить установленное приложение",
        ["List discovered apps"] = "Показать найденные приложения",
        ["Enable, disable, or check Windows startup"] = "Настроить автозапуск Windows",
        ["Search Google"] = "Искать в Google",
        ["Search YouTube"] = "Искать на YouTube",
        ["Search Twitch"] = "Искать на Twitch",
        ["Search public Discord pages"] = "Искать общедоступные страницы Discord",
        ["Search Reddit"] = "Искать на Reddit",
        ["Search GitHub"] = "Искать на GitHub",
        ["Gracefully close a named app"] = "Корректно закрыть приложение по имени",
        ["Open a folder by path"] = "Открыть папку по пути",
        ["Get or set system volume (0-100, up, down)"] = "Узнать или изменить громкость (0–100, вверх, вниз)",
        ["Toggle system audio mute"] = "Включить или выключить звук",
        ["Open Google Translate"] = "Открыть Google Переводчик",
        ["Show local IP addresses"] = "Показать локальные IP-адреса",
        ["Use: /close app name."] = "Используйте: /close название приложения.",
        ["No matching open app was found for '{0}'."] = "Не найдено открытое приложение «{0}».",
        ["More than one window matches '{0}'. Use a more specific name."] = "Найдено несколько окон по запросу «{0}». Уточните название.",
        ["Close request sent to {0}."] = "Отправлен запрос на закрытие: {0}.",
        ["The app did not accept a close request."] = "Приложение не приняло запрос на закрытие.",
        ["Could not close the app: {0}"] = "Не удалось закрыть приложение: {0}",
        ["Use: /folder path to a folder."] = "Используйте: /folder путь к папке.",
        ["Folder not found: {0}"] = "Папка не найдена: {0}",
        ["Could not open folder: {0}"] = "Не удалось открыть папку: {0}",
        ["Volume: {0}%"] = "Громкость: {0}%",
        ["Use: /volume 0-100, /volume up, or /volume down."] = "Используйте: /volume 0–100, /volume up или /volume down.",
        ["Volume set to {0}%."] = "Громкость установлена на {0}%.",
        ["Volume changed to {0}%."] = "Громкость изменена на {0}%.",
        ["System audio is muted."] = "Звук выключен.",
        ["System audio is unmuted."] = "Звук включён.",
        ["Could not change system volume: {0}"] = "Не удалось изменить системную громкость: {0}",
        ["Opening Google Translate."] = "Открываем Google Переводчик.",
        ["Could not open Google Translate: {0}"] = "Не удалось открыть Google Переводчик: {0}",
        ["No local IPv4 addresses were found."] = "Локальные IPv4-адреса не найдены.",
        ["Local IP addresses: {0}"] = "Локальные IP-адреса: {0}",
        ["Could not read local IP addresses: {0}"] = "Не удалось получить локальные IP-адреса: {0}",
        ["Found apps: {0}"] = "Найденные приложения: {0}",
        ["{0} apps discovered"] = "Найдено приложений: {0}",
        ["{0} note"] = "Заметок: {0}",
        ["{0} notes"] = "Заметок: {0}",
        ["{0} item"] = "Элементов: {0}",
        ["{0} items"] = "Элементов: {0}",
        ["Showing {0} saved note(s)."] = "Показано заметок: {0}.",
        ["Showing {0} clipboard item(s)."] = "Показано элементов буфера обмена: {0}.",
        ["Opening {0}."] = "Открываем: {0}.",
        ["Launching {0}."] = "Запускаем: {0}.",
        ["Could not open {0}."] = "Не удалось открыть: {0}.",
        ["Could not open the browser: {0}"] = "Не удалось открыть браузер: {0}",
        ["Opening {0} search results in your browser."] = "Открываем результаты поиска {0} в браузере.",
        ["Could not update Windows startup: {0}"] = "Не удалось изменить автозапуск Windows: {0}",
        ["Could not save language preference: {0}"] = "Не удалось сохранить выбор языка: {0}",
        ["Enter an app name, for example: unity, vscode, steam, or spotify."] = "Введите название приложения, например unity, vscode, steam или spotify.",
        ["Could not launch '{0}'. Select an app from the suggestions or enter an executable path."] = "Не удалось запустить «{0}». Выберите приложение из списка или укажите путь к файлу.",
        ["Enter a search term after {0}."] = "Введите поисковый запрос после {0}.",
        ["Countdown started for {0}."] = "Таймер запущен на {0}.",
        ["Session started: {0}"] = "Сеанс начат: {0}",
        ["Type a note after the note command."] = "Введите текст заметки после команды /note.",
        ["Language changed to English."] = "Язык изменён на английский.",
        ["Language changed to Russian."] = "Язык изменён на русский.",
        ["QUICK APPS"] = "БЫСТРЫЙ ЗАПУСК",
        ["Scanning for apps..."] = "Поиск приложений...",
        ["No apps available yet."] = "Приложения пока недоступны.",
        ["Unknown command: {0}"] = "Неизвестная команда: {0}",
        ["{0} apps available"] = "Доступно приложений: {0}",
        ["Choose a command to add it to the search box."] = "Выберите команду, чтобы добавить её в строку поиска."
    };
    private string _lastClipboard = "";
    private bool _clipboardCaptureEnabled = true;
    private bool _isLoadingClipboardPrivacySettings = true;
    private uint? _clipboardSequenceBaseline;
    private readonly HashSet<string> _excludedClipboardApplications = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _countdownEndsAt;
    private TimeSpan _sessionElapsed;
    private DateTime _sessionStartedAt;
    private int _scanInProgress;
    private bool _applicationScanCompleted;
    private bool _allowWindowClose;
    private bool _settingsAnimating;
    private bool _commandsAnimating;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        LoadLanguagePreference();
        LoadThemePreference();
        LoadAppUsage();
        LoadClipboardPrivacySettings();
        ApplyLanguage();
        UpdateClock();
        _ = new WindowInteropHelper(this).EnsureHandle();
        LoadNotes();
        Dispatcher.BeginInvoke(StartApplicationScan, DispatcherPriority.Background);
        _ticker.Tick += TickerTick;
        _ticker.Start();
        _clipboardTicker.Tick += (_, _) =>
        {
            if (IsVisible && WindowState != WindowState.Minimized)
                CaptureClipboard();
        };
        _clipboardTicker.Start();
        _scrollAnimationTimer.Tick += ScrollAnimationTimer_Tick;
        var startHidden = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
        try
        {
            var localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var flowDeckPath = Path.Combine(localDataPath, "FlowDeck");
            Directory.CreateDirectory(flowDeckPath);
            File.AppendAllText(
                Path.Combine(flowDeckPath, "startup.log"),
                $"{DateTime.Now:O} startHidden={startHidden}; args={string.Join("|", Environment.GetCommandLineArgs())}{Environment.NewLine}");
        }

        catch (Exception exception)
        {
            LogStartupFailure(exception);
        }

        if (!startHidden)
        {
            Dispatcher.BeginInvoke(() =>
            {
                Show();
                Activate();
                AnimateOpen();
            }, DispatcherPriority.Loaded);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource.AddHook(WindowMessageHook);
        RegisterHotKey(_windowSource.Handle, HotkeyId, ModControl | ModShift, VkSpace);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_windowSource != null)
        {
            UnregisterHotKey(_windowSource.Handle, HotkeyId);
            _windowSource.RemoveHook(WindowMessageHook);
        }

        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowWindowClose)
        {
            e.Cancel = true;
            if (!_isAnimating)
            {
                _allowWindowClose = true;
                AnimateClose(Application.Current.Shutdown);
            }
        }

        base.OnClosing(e);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Dispatcher.Invoke(ToggleWindow);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleWindow()
    {
        if (_isAnimating)
            return;

        if (IsVisible && WindowState != WindowState.Minimized)
        {
            BackgroundGradientAnimation.Stop(this);
            AnimateClose(() => Hide());
            return;
        }

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        Activate();
        AnimateOpen();
        BackgroundGradientAnimation.Begin(this, true);
    }

    internal void ActivateFromSecondInstance()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            if (!IsVisible)
            {
                Show();
                AnimateOpen();
                BackgroundGradientAnimation.Begin(this, true);
            }

            Activate();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void AnimateOpen()
    {
        _isAnimating = true;
        WindowSurface.BeginAnimation(UIElement.OpacityProperty, null);
        WindowSurface.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WindowSurface.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        WindowSurface.Opacity = 0;
        var transform = (ScaleTransform)WindowSurface.RenderTransform;
        transform.ScaleX = 0.96;
        transform.ScaleY = 0.96;

        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateDoubleAnimation(WindowSurface, UIElement.OpacityProperty, 0, 1, 0.28, easing));
        storyboard.Children.Add(CreateDoubleAnimation(transform, ScaleTransform.ScaleXProperty, 0.96, 1, 0.32, easing));
        storyboard.Children.Add(CreateDoubleAnimation(transform, ScaleTransform.ScaleYProperty, 0.96, 1, 0.32, easing));
        storyboard.Completed += (_, _) => _isAnimating = false;
        storyboard.Begin();
    }

    private void AnimateClose(Action completedAction)
    {
        _isAnimating = true;
        var transform = (ScaleTransform)WindowSurface.RenderTransform;
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateDoubleAnimation(WindowSurface, UIElement.OpacityProperty, WindowSurface.Opacity, 0, 0.22, easing));
        storyboard.Children.Add(CreateDoubleAnimation(transform, ScaleTransform.ScaleXProperty, transform.ScaleX, 0.96, 0.22, easing));
        storyboard.Children.Add(CreateDoubleAnimation(transform, ScaleTransform.ScaleYProperty, transform.ScaleY, 0.96, 0.22, easing));
        storyboard.Completed += (_, _) =>
        {
            _isAnimating = false;
            completedAction();
        };
        storyboard.Begin();
    }

    private static DoubleAnimation CreateDoubleAnimation(DependencyObject target, DependencyProperty property,
        double from, double to, double seconds, IEasingFunction easing)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        return animation;
    }

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Down || e.Key == Key.Up) && AppSuggestions.Visibility == Visibility.Visible)
        {
            var direction = e.Key == Key.Down ? 1 : -1;
            var index = AppSuggestions.SelectedIndex + direction;
            if (index < 0) index = AppSuggestions.Items.Count - 1;
            if (index >= AppSuggestions.Items.Count) index = 0;
            _navigatingSuggestions = true;
            AppSuggestions.SelectedIndex = index;
            _navigatingSuggestions = false;
            if (AppSuggestions.SelectedItem is FlowDeckCommand selectedCommand)
            {
                _selectingSuggestion = true;
                CommandBox.Text = selectedCommand.Name;
                CommandBox.CaretIndex = CommandBox.Text.Length;
                _selectingSuggestion = false;
            }
            else if (AppSuggestions.SelectedItem is DiscoveredApp selectedApp)
            {
                _selectingSuggestion = true;
                CommandBox.Text = selectedApp.Name;
                CommandBox.CaretIndex = CommandBox.Text.Length;
                _selectingSuggestion = false;
            }
            Dispatcher.BeginInvoke(ScrollSelectedSuggestionIntoView, DispatcherPriority.Loaded);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            RunCommand();
            e.Handled = true;
        }
    }

    private void RunCommand_Click(object sender, RoutedEventArgs e) => RunCommand();

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CacheFontSizeBaselines(WindowSurface);
        UpdateResponsiveFontSizes();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveFontSizes();

    private void CacheFontSizeBaselines(DependencyObject parent)
    {
        if (parent is FrameworkElement element &&
            (element is Control || element is TextBlock) &&
            !_fontSizeBaselines.ContainsKey(element))
        {
            _fontSizeBaselines.Add(element, element switch
            {
                Control control => control.FontSize,
                TextBlock textBlock => textBlock.FontSize,
                _ => 12
            });
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            CacheFontSizeBaselines(VisualTreeHelper.GetChild(parent, index));
    }

    private void UpdateResponsiveFontSizes()
    {
        if (!IsLoaded || WindowSurface.ActualWidth <= 0 || WindowSurface.ActualHeight <= 0)
            return;

        ApplyResponsiveFontSizes(WindowSurface, GetResponsiveFontScale());
    }

    private double GetResponsiveFontScale() =>
        Math.Clamp(
            Math.Min(WindowSurface.ActualWidth / 980, WindowSurface.ActualHeight / 620),
            0.75,
            1.0);

    private void ApplyResponsiveFontSizes(DependencyObject parent, double scale)
    {
        if (parent is FrameworkElement element)
        {
            if (!_fontSizeBaselines.ContainsKey(element) &&
                ((element is TextBlock && element.ReadLocalValue(TextBlock.FontSizeProperty) is double) ||
                 (element is Control && element.ReadLocalValue(Control.FontSizeProperty) is double)))
            {
                _fontSizeBaselines.Add(element, element switch
                {
                    Control control => control.FontSize,
                    TextBlock textBlock => textBlock.FontSize,
                    _ => 12
                });
            }

            if (_fontSizeBaselines.TryGetValue(element, out var baseline))
            {
                var fontSize = Math.Max(8, baseline * scale);
                switch (element)
                {
                    case Control control:
                        control.FontSize = fontSize;
                        break;
                    case TextBlock textBlock:
                        textBlock.FontSize = fontSize;
                        break;
                }
            }
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            ApplyResponsiveFontSizes(VisualTreeHelper.GetChild(parent, index), scale);
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        _isRussian = !_isRussian;
        ApplyLanguage();
        try
        {
            _storageService.SaveLanguage(_isRussian ? "ru" : "en");
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not save language preference: {0}", exception.Message);
        }
    }

    private void LoadLanguagePreference()
    {
        try
        {
            var savedLanguage = _storageService.LoadLanguage()?.Trim();
            _isRussian = savedLanguage?.Equals("ru", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not load language preference: {exception.Message}",
                "FlowDeck",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadThemePreference()
    {
        try
        {
            var theme = _storageService.LoadTheme()?.Trim();
            if (string.IsNullOrWhiteSpace(theme))
                return;

            ThemeMenuButton.Content = $"{theme}  ▾";
            ApplyTheme(theme);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load theme preference: {exception}");
        }
    }

    private void SaveThemePreference(string theme)
    {
        try
        {
            _storageService.SaveTheme(theme);
        }
        catch (Exception exception)
        {
            ResultText.Text = $"Could not save theme preference: {exception.Message}";
        }
    }

    private void LoadAppUsage()
    {
        try
        {
            var saved = _storageService.LoadAppUsage();
            if (saved == null)
                return;

            foreach (var item in saved)
                _appLaunchCounts[item.Key] = item.Value;
        }
        catch
        {
        }
    }

    private void LoadClipboardPrivacySettings()
    {
        try
        {
            var settings = _storageService.LoadClipboardPrivacySettings();
            if (settings != null)
            {
                _clipboardCaptureEnabled = settings.CaptureEnabled;
                _excludedClipboardApplications.Clear();
                foreach (var application in settings.ExcludedApplications ?? [])
                {
                    var processName = NormalizeApplicationName(application);
                    if (!string.IsNullOrWhiteSpace(processName))
                        _excludedClipboardApplications.Add(processName);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                TFormat("Could not load clipboard privacy settings: {0}", exception.Message),
                "FlowDeck",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            ClipboardCaptureToggle.IsChecked = _clipboardCaptureEnabled;
            ClipboardExcludedAppsBox.Text = string.Join(Environment.NewLine, _excludedClipboardApplications.OrderBy(name => name));
            _isLoadingClipboardPrivacySettings = false;
        }
    }

    private bool SaveClipboardPrivacySettings()
    {
        try
        {
            _storageService.SaveClipboardPrivacySettings(new ClipboardPrivacySettings
            {
                CaptureEnabled = _clipboardCaptureEnabled,
                ExcludedApplications = _excludedClipboardApplications.OrderBy(name => name).ToArray()
            });
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Text.Json.JsonException)
        {
            ResultText.Text = TFormat("Could not save clipboard privacy settings: {0}", exception.Message);
            return false;
        }
    }

    private static string NormalizeApplicationName(string applicationName) =>
        Path.GetFileNameWithoutExtension(applicationName.Trim().Trim('"')).Trim();

    private void ClipboardCapture_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingClipboardPrivacySettings)
            return;

        _clipboardCaptureEnabled = ClipboardCaptureToggle.IsChecked == true;
        _clipboardSequenceBaseline = _clipboardPrivacyService.GetClipboardSequenceNumber();
        if (SaveClipboardPrivacySettings())
        {
            ResultText.Text = T(_clipboardCaptureEnabled
                ? "Clipboard capture enabled."
                : "Clipboard capture disabled. Existing history is unchanged.");
        }
    }

    private void SaveClipboardExclusions_Click(object sender, RoutedEventArgs e)
    {
        var applicationNames = ClipboardExcludedAppsBox.Text
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeApplicationName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _excludedClipboardApplications.Clear();
        _excludedClipboardApplications.UnionWith(applicationNames);
        ClipboardExcludedAppsBox.Text = string.Join(Environment.NewLine, _excludedClipboardApplications.OrderBy(name => name));
        if (SaveClipboardPrivacySettings())
            ResultText.Text = T("Clipboard exclusions saved.");
    }

    private void ClearClipboardHistory_Click(object sender, RoutedEventArgs e)
    {
        _clipboardHistory.Clear();
        _lastClipboard = "";
        _clipboardSequenceBaseline = _clipboardPrivacyService.GetClipboardSequenceNumber();
        ClipboardCountText.Text = FormatClipboardCount(0);
        if (_historyView == "clipboard")
            _viewModel.SetHistory([T("Copy some text and it will appear here.")]);
        ResultText.Text = T("Clipboard history cleared.");
    }

    private void TrackAppLaunch(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return;

        _appLaunchCounts.TryGetValue(displayName, out var count);
        _appLaunchCounts[displayName] = count + 1;

        try
        {
            _storageService.SaveAppUsage(_appLaunchCounts);
        }
        catch
        {
        }
    }

    private void RefreshCommandsList()
    {
        _viewModel.SetCommands(BuiltInCommands
            .Concat(SearchProviders)
            .Select(command => command with { Description = T(command.Description) })
            .ToList());
    }

    private void CommandsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandsList.SelectedItem is not FlowDeckCommand command)
            return;

        CommandsList.SelectedItem = null;
        CommandBox.Text = command.Name + " ";
        CommandBox.CaretIndex = CommandBox.Text.Length;
        CommandBox.Focus();
        UpdateAppSuggestions();
        AnimateCommandsPanel(false);
    }

    private string T(string text) =>
        _isRussian && RussianText.TryGetValue(text, out var translation) ? translation : text;

    private string TFormat(string format, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(format), values);

    private void ApplyLanguage()
    {
        var languageWasInitialized = _languageInitialized;
        LanguageButton.Content = _isRussian ? "EN" : "RU";
        LanguageButton.ToolTip = T("Switch language");
        CloseButton.ToolTip = T("Close");
        AppDiscoveryTitle.Text = T("APP DISCOVERY");
        AppDiscoveryDescription.Text = T("Searching installed .exe files and Start Menu shortcuts.");
        QuickAppsTitle.Text = T("QUICK APPS");
        QuickAppsEmptyText.Text = !_applicationScanCompleted
            ? T("Scanning for apps...")
            : _discoveredApps.Count == 0
                ? T("No apps available yet.")
                : TFormat("{0} apps available", _discoveredApps.Count);
        MainHeading.Text = T("What do you want to do?");
        MainSubheading.Text = T("Try typing a command or an app name");
        RefreshCommandsList();
        ResultText.Text = T("Ready. Your command results will appear here.");
        SessionTitle.Text = T("Session");
        StopButton.Content = T("Stop");
        ScratchpadTitle.Text = T("Scratchpad");
        ViewNotesButton.Content = T("View notes");
        ClipboardTitle.Text = T("Clipboard");
        ViewHistoryButton.Content = T("View history");
        ClipboardPrivacyTitle.Text = T("PRIVACY");
        ClipboardCaptureToggle.Content = T("Capture clipboard history");
        ClipboardPrivacyDescription.Text = T("History is kept in memory only. Windows opt-out-marked content is ignored.");
        ClipboardExcludedAppsTitle.Text = T("Excluded applications (process names)");
        ClipboardExcludedAppsDescription.Text = T("Separate names with commas or new lines; for example, password-manager.exe");
        SaveClipboardExclusionsButton.Content = T("Save exclusions");
        ClearClipboardHistoryButton.Content = T("Clear clipboard history");
        ClearClipboardHistoryButton.Visibility = _historyView == "clipboard" ? Visibility.Visible : Visibility.Collapsed;
        HistorySectionTitle.Text = T(_historyView == "notes" ? "Notes" : "Clipboard history");
        _viewModel.SetHistory(_historyView == "notes"
            ? _notes.Count == 0 ? [T("No notes yet. Try: /note remember to fix flashlight")] : _notes
            : _clipboardHistory.Count == 0 ? [T("Copy some text and it will appear here.")] : _clipboardHistory);
        DiscoveredAppCountText.Text = _applicationScanCompleted
            ? TFormat("{0} apps discovered", _discoveredApps.Count)
            : T("Scanning apps...");
        NotesCountText.Text = FormatNoteCount(_notes.Count);
        ClipboardCountText.Text = FormatClipboardCount(_clipboardHistory.Count);
        ResultText.Text = languageWasInitialized
            ? T(_isRussian ? "Language changed to Russian." : "Language changed to English.")
            : T("Ready. Your command results will appear here.");
        _languageInitialized = true;
        UpdateAppSuggestions();
    }

    private string FormatNoteCount(int count) =>
        _isRussian
            ? FormatCount(count, "заметка", "заметки", "заметок")
            : $"{count} note{(count == 1 ? "" : "s")}";

    private string FormatClipboardCount(int count) =>
        _isRussian
            ? FormatCount(count, "элемент", "элемента", "элементов")
            : $"{count} item{(count == 1 ? "" : "s")}";

    private string FormatCount(int count, string singular, string few, string many)
    {
        var absolute = Math.Abs(count);
        var value = absolute % 10;
        var tens = absolute % 100;

        if (count == 0 || value == 0 || (value >= 5 && value <= 9) || (tens >= 11 && tens <= 14))
            return $"{count} {many}";

        if (value == 1 && tens != 11)
            return $"{count} {singular}";

        if (value >= 2 && value <= 4 && !(tens >= 12 && tens <= 14))
            return $"{count} {few}";

        return $"{count} {many}";
    }

    private void RunCommand()
    {
        var command = CommandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return;

        var parts = command.Split(' ', 2, StringSplitOptions.TrimEntries);
        var action = parts[0].TrimStart('/').ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : "";

        switch (action)
        {
            case "unity": LaunchApp("Unity Hub", "unityhub://", "UnityHub.exe"); break;
            case "vscode":
            case "code": LaunchApp("Visual Studio Code", "code", "Code.exe"); break;
            case "steam": LaunchApp("Steam", "steam://open/main", "steam.exe"); break;
            case "spotify": LaunchApp("Spotify", "spotify:", "Spotify.exe"); break;
            case "note": AddNote(argument); break;
            case "notes": ViewNotes(); break;
            case "search": SearchWeb(argument); break;
            case "close": CloseNamedApp(argument); break;
            case "folder": OpenFolder(argument); break;
            case "open":
                if (argument.Equals("folder", StringComparison.OrdinalIgnoreCase))
                    ResultText.Text = T("Use: /folder path to a folder.");
                else if (argument.StartsWith("folder ", StringComparison.OrdinalIgnoreCase))
                    OpenFolder(argument["folder ".Length..].Trim());
                else
                    ResultText.Text = T("Use: /folder path to a folder.");
                break;
            case "volume": ChangeVolume(argument); break;
            case "mute": ToggleMute(); break;
            case "translate": OpenTranslator(); break;
            case "ip": ShowLocalIpAddresses(); break;
            case "timer": StartTimer(argument); break;
            case "session": StartSession(argument); break;
            case "stop": StopTimer(); break;
            case "calc":
            case "calculate": Calculate(argument); break;
            case "clipboard": ViewClipboard(); break;
            case "focus": ToggleFocus(argument); break;
            case "apps": ShowDiscoveredApps(); break;
            case "startup": ConfigureStartup(argument); break;
            default:
                if (command.StartsWith('/'))
                    ResultText.Text = TFormat("Unknown command: {0}", parts[0]);
                else
                    LaunchDiscoveredApp(command);
                break;
        }

        CommandBox.SelectAll();
    }

    private void LaunchApp(string displayName, string uri, string executable)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
            ResultText.Text = TFormat("Opening {0}.", displayName);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
                ResultText.Text = TFormat("Opening {0}.", displayName);
            }
            catch (Exception executableException)
            {
                ResultText.Text = TFormat("Could not open {0}.", $"{displayName}: {executableException.Message}");
            }
        }
    }

    private void LaunchByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ResultText.Text = T("Enter an app name, for example: unity, vscode, steam, or spotify.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(name) { UseShellExecute = true });
            ResultText.Text = TFormat("Launching {0}.", name);
        }
        catch
        {
            ResultText.Text = TFormat("Could not launch '{0}'. Select an app from the suggestions or enter an executable path.", name);
        }
    }

    private void CloseNamedApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            ResultText.Text = T("Use: /close app name.");
            return;
        }

        try
        {
            var processes = Process.GetProcesses();
            try
            {
                var matchingProcesses = processes
                    .Where(process => process.MainWindowHandle != IntPtr.Zero &&
                        (Path.GetFileNameWithoutExtension(process.ProcessName).Equals(
                             Path.GetFileNameWithoutExtension(appName), StringComparison.OrdinalIgnoreCase) ||
                         process.MainWindowTitle.Contains(appName, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (matchingProcesses.Length == 0)
                {
                    ResultText.Text = TFormat("No matching open app was found for '{0}'.", appName);
                    return;
                }

                if (matchingProcesses.Length > 1)
                {
                    ResultText.Text = TFormat("More than one window matches '{0}'. Use a more specific name.", appName);
                    return;
                }

                var process = matchingProcesses[0];
                ResultText.Text = process.CloseMainWindow()
                    ? TFormat("Close request sent to {0}.", process.MainWindowTitle)
                    : T("The app did not accept a close request.");
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not close the app: {0}", exception.Message);
        }
    }

    private void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            ResultText.Text = T("Use: /folder path to a folder.");
            return;
        }

        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(folderPath.Trim('"'));
            var fullPath = Path.GetFullPath(expandedPath);
            if (!Directory.Exists(fullPath))
            {
                ResultText.Text = TFormat("Folder not found: {0}", fullPath);
                return;
            }

            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(fullPath);
            Process.Start(startInfo);
            ResultText.Text = TFormat("Opening {0}.", fullPath);
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not open folder: {0}", exception.Message);
        }
    }

    private void ChangeVolume(string argument)
    {
        try
        {
            var currentVolume = WindowsAudioController.GetVolume();
            if (string.IsNullOrWhiteSpace(argument))
            {
                ResultText.Text = TFormat("Volume: {0}%", (int)Math.Round(currentVolume * 100));
                return;
            }

            var action = argument.Trim();
            var newVolume = action.ToLowerInvariant() switch
            {
                "up" => Math.Min(1, currentVolume + 0.05f),
                "down" => Math.Max(0, currentVolume - 0.05f),
                _ when int.TryParse(action, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
                    && percent is >= 0 and <= 100 => percent / 100f,
                _ => -1
            };

            if (newVolume < 0)
            {
                ResultText.Text = T("Use: /volume 0-100, /volume up, or /volume down.");
                return;
            }

            WindowsAudioController.SetVolume(newVolume);
            ResultText.Text = TFormat(
                action.Equals("up", StringComparison.OrdinalIgnoreCase) || action.Equals("down", StringComparison.OrdinalIgnoreCase)
                    ? "Volume changed to {0}%."
                    : "Volume set to {0}%.",
                (int)Math.Round(newVolume * 100));
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not change system volume: {0}", exception.Message);
        }
    }

    private void ToggleMute()
    {
        try
        {
            var isMuted = WindowsAudioController.GetMute();
            WindowsAudioController.SetMute(!isMuted);
            ResultText.Text = T(!isMuted ? "System audio is muted." : "System audio is unmuted.");
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not change system volume: {0}", exception.Message);
        }
    }

    private void OpenTranslator()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://translate.google.com/") { UseShellExecute = true });
            ResultText.Text = T("Opening Google Translate.");
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not open Google Translate: {0}", exception.Message);
        }
    }

    private void ShowLocalIpAddresses()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address))
                .Select(address => address.Address.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();

            ResultText.Text = addresses.Length == 0
                ? T("No local IPv4 addresses were found.")
                : TFormat("Local IP addresses: {0}", string.Join(", ", addresses));
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not read local IP addresses: {0}", exception.Message);
        }
    }

    private void RescanApps_Click(object sender, RoutedEventArgs e)
    {
        StartApplicationScan();
    }

    private void QuickAppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuickAppList.SelectedItem is not DiscoveredApp app)
            return;

        QuickAppList.SelectedItem = null;
        LaunchApplication(app.Path, app.Name);
        RefreshQuickApps();
    }

    private void RefreshQuickApps()
    {
        _viewModel.QuickApps.Clear();
        var quickApps = _discoveredApps
            .OrderByDescending(app => _appLaunchCounts.TryGetValue(app.Name, out var count) ? count : 0)
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Take(8);
        foreach (var app in quickApps)
        {
            _viewModel.QuickApps.Add(app);
        }

        AnimateQuickAppList();
    }

    private void AnimateQuickAppList()
    {
        QuickAppList.BeginAnimation(UIElement.OpacityProperty, null);
        QuickAppListOffset.BeginAnimation(TranslateTransform.YProperty, null);

        QuickAppList.Opacity = 0;
        QuickAppListOffset.Y = 8;

        var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var offset = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase
            {
                Amplitude = 0.12,
                EasingMode = EasingMode.EaseOut
            }
        };

        QuickAppList.BeginAnimation(UIElement.OpacityProperty, opacity);
        QuickAppListOffset.BeginAnimation(TranslateTransform.YProperty, offset);
    }

    private void QuickAppList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        QuickAppList.ApplyTemplate();
        var scrollViewer = FindVisualChild<ScrollViewer>(QuickAppList);
        if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0)
            return;

        var start = ReferenceEquals(_scrollAnimationViewer, scrollViewer) && _scrollAnimationTimer.IsEnabled
            ? _scrollAnimationTarget
            : scrollViewer.VerticalOffset;
        var target = Math.Clamp(start - e.Delta / 3d, 0, scrollViewer.ScrollableHeight);
        if (Math.Abs(target - start) >= 0.5)
        {
            AnimateScrollTo(scrollViewer, target);
            e.Handled = true;
        }
    }

    private void ConfigureStartup(string argument)
    {
        try
        {
            var action = argument.ToLowerInvariant();
            if (action == "status")
            {
                ResultText.Text = !_startupService.IsEnabled()
                    ? T("FlowDeck is not configured to start with Windows.")
                    : T("FlowDeck is configured to start with Windows.");
                return;
            }

            if (action == "off" || action == "disable")
            {
                _startupService.Configure(action);
                ResultText.Text = T("FlowDeck will no longer start with Windows.");
                return;
            }

            if (action != "on" && action != "enable" && !string.IsNullOrWhiteSpace(action))
            {
                ResultText.Text = T("Use: /startup on, /startup off, or /startup status.");
                return;
            }

            _startupService.Configure(action);
            ResultText.Text = T("FlowDeck will start with Windows.");
        }
        catch (Exception exception)
        {
            ResultText.Text = exception is InvalidOperationException
                ? T("Could not determine the FlowDeck executable path.")
                : TFormat("Could not update Windows startup: {0}", exception.Message);
        }
    }

    private void CommandBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_selectingSuggestion) return;
        UpdateAppSuggestions();
    }

    private void AppSuggestions_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_selectingSuggestion || _navigatingSuggestions) return;

        if (AppSuggestions.SelectedItem is DiscoveredApp app)
        {
            AppSuggestions.SelectedItem = null;
            AnimateSuggestions(false);
            LaunchApplication(app.Path, app.Name);
            RefreshQuickApps();
            return;
        }

        _selectingSuggestion = true;
        CommandBox.Text = AppSuggestions.SelectedItem switch
        {
            FlowDeckCommand command => command.Name,
            _ => CommandBox.Text
        };
        CommandBox.CaretIndex = CommandBox.Text.Length;
        AnimateSuggestions(false);
        _selectingSuggestion = false;
    }

    private void ScrollSelectedSuggestionIntoView()
    {
        if (AppSuggestions.SelectedItem == null)
            return;

        AppSuggestions.ApplyTemplate();
        AppSuggestions.UpdateLayout();
        var scrollViewer = FindVisualChild<ScrollViewer>(AppSuggestions);
        var item = AppSuggestions.ItemContainerGenerator.ContainerFromItem(AppSuggestions.SelectedItem) as FrameworkElement;
        if (scrollViewer == null || item == null)
            return;

        var itemTop = item.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
        var target = scrollViewer.VerticalOffset;
        if (itemTop < 0)
            target += itemTop;
        else if (itemTop + item.ActualHeight > scrollViewer.ViewportHeight)
            target += itemTop + item.ActualHeight - scrollViewer.ViewportHeight;

        target = Math.Clamp(target, 0, scrollViewer.ScrollableHeight);
        if (Math.Abs(target - scrollViewer.VerticalOffset) < 0.5)
            return;

        AnimateScrollTo(scrollViewer, target);
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0)
            return;

        while (source != null)
        {
            if (source is ScrollViewer scrollViewer)
            {
                var isSuggestionList = AppSuggestions.IsAncestorOf(scrollViewer);
                var isQuickList = QuickAppList.IsAncestorOf(scrollViewer) ||
                    CommandsList.IsAncestorOf(scrollViewer);
                var scrollAmount = lines == -1
                    ? scrollViewer.ViewportHeight
                    : isSuggestionList ? 46 : isQuickList ? 40 : lines * 40;
                var offsetDelta = -e.Delta / 120d * scrollAmount;
                var start = ReferenceEquals(_scrollAnimationViewer, scrollViewer) && _scrollAnimationTimer.IsEnabled
                    ? _scrollAnimationTarget
                    : scrollViewer.VerticalOffset;
                var target = Math.Clamp(start + offsetDelta, 0, scrollViewer.ScrollableHeight);
                if (Math.Abs(target - start) >= 0.5)
                {
                    AnimateScrollTo(scrollViewer, target);
                    e.Handled = true;
                    return;
                }
            }

            source = VisualTreeHelper.GetParent(source);
        }

        if (MainScrollViewer.ScrollableHeight > 0)
        {
            var scrollAmount = lines == -1 ? MainScrollViewer.ViewportHeight : lines * 40;
            var offsetDelta = -e.Delta / 120d * scrollAmount;
            var start = ReferenceEquals(_scrollAnimationViewer, MainScrollViewer) && _scrollAnimationTimer.IsEnabled
                ? _scrollAnimationTarget
                : MainScrollViewer.VerticalOffset;
            var target = Math.Clamp(start + offsetDelta, 0, MainScrollViewer.ScrollableHeight);
            if (Math.Abs(target - start) >= 0.5)
            {
                AnimateScrollTo(MainScrollViewer, target);
                e.Handled = true;
            }
        }
    }

    private void AnimateScrollTo(ScrollViewer scrollViewer, double target)
    {
        if (!ReferenceEquals(_scrollAnimationViewer, scrollViewer))
            _scrollAnimationVelocity = 0;

        _scrollAnimationViewer = scrollViewer;
        _scrollAnimationTarget = Math.Clamp(target, 0, scrollViewer.ScrollableHeight);
        _scrollAnimationLastTimestamp = Stopwatch.GetTimestamp();
        _scrollAnimationTimer.Start();
    }

    private void ScrollAnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_scrollAnimationViewer == null)
        {
            _scrollAnimationTimer.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = Math.Clamp(
            (now - _scrollAnimationLastTimestamp) / (double)Stopwatch.Frequency,
            0.001,
            0.05);
        _scrollAnimationLastTimestamp = now;

        var currentOffset = _scrollAnimationViewer.VerticalOffset;
        var distance = _scrollAnimationTarget - currentOffset;
        const double stiffness = 220;
        const double damping = 30;
        var acceleration = distance * stiffness - _scrollAnimationVelocity * damping;
        _scrollAnimationVelocity += acceleration * elapsedSeconds;
        var nextOffset = currentOffset + _scrollAnimationVelocity * elapsedSeconds;

        if (Math.Abs(distance) < 0.35 && Math.Abs(_scrollAnimationVelocity) < 3)
        {
            _scrollAnimationViewer.ScrollToVerticalOffset(_scrollAnimationTarget);
            _scrollAnimationVelocity = 0;
            _scrollAnimationTimer.Stop();
            _scrollAnimationViewer = null;
            return;
        }

        _scrollAnimationViewer.ScrollToVerticalOffset(nextOffset);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result)
                return result;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private void SearchWeb(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ResultText.Text = T("Choose a site and enter a search term, for example: /search youtube music.");
            return;
        }

        var searchParts = query.Split(' ', 2, StringSplitOptions.TrimEntries);
        var provider = searchParts[0].ToLowerInvariant();
        var searchTerm = searchParts.Length > 1 ? searchParts[1] : "";
        if (SearchProviders.All(option => !option.Name.EndsWith($" {provider}", StringComparison.OrdinalIgnoreCase)))
        {
            searchTerm = query;
            provider = "google";
        }

        var searchUrl = string.IsNullOrWhiteSpace(searchTerm)
            ? provider switch
            {
                "youtube" => "https://www.youtube.com/",
                "twitch" => "https://www.twitch.tv/",
                "discord" => "https://discord.com/",
                "reddit" => "https://www.reddit.com/",
                "github" => "https://github.com/",
                _ => "https://www.google.com/"
            }
            : BuildSearchUrl(provider, searchTerm);

        try
        {
            Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
            ResultText.Text = string.IsNullOrWhiteSpace(searchTerm)
                ? TFormat("Opening {0}.", provider)
                : provider == "discord"
                    ? T("Opening public Discord web results in your browser. Private Discord content is not searchable this way.")
                    : TFormat("Opening {0} search results in your browser.", provider);
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not open the browser: {0}", exception.Message);
        }
    }

    private static string BuildSearchUrl(string provider, string searchTerm)
    {
        var encodedQuery = Uri.EscapeDataString(searchTerm);
        return provider switch
        {
            "youtube" => $"https://www.youtube.com/results?search_query={encodedQuery}",
            "twitch" => $"https://www.twitch.tv/search?term={encodedQuery}",
            "discord" => $"https://www.google.com/search?q=site%3Adiscord.com+{encodedQuery}",
            "reddit" => $"https://www.reddit.com/search/?q={encodedQuery}",
            "github" => $"https://github.com/search?q={encodedQuery}",
            _ => $"https://www.google.com/search?q={encodedQuery}"
        };
    }

    private void UpdateAppSuggestions()
    {
        var rawQuery = CommandBox.Text.TrimStart();
        var query = rawQuery.Trim();
        if (query.StartsWith('/'))
        {
            if (rawQuery.StartsWith("/search ", StringComparison.OrdinalIgnoreCase))
            {
                var searchRemainder = rawQuery["/search ".Length..];
                var providerFilter = searchRemainder.Split(' ', 2)[0];
                var providers = SearchProviders
                    .Where(provider => providerFilter.Length == 0 ||
                        provider.Name.Split(' ').Last().StartsWith(providerFilter, StringComparison.OrdinalIgnoreCase))
                    .Select(provider => provider with { Description = T(provider.Description) })
                    .ToArray();
                _viewModel.SetSuggestions(providers);
                AnimateSuggestions(!searchRemainder.Contains(' '));
                return;
            }

            var commandFilter = query[1..];
            var commands = BuiltInCommands
                .Where(command => commandFilter.Length == 0 ||
                    command.Name.Contains(commandFilter, StringComparison.OrdinalIgnoreCase))
                .Take(20)
                .Select(command => command with { Description = T(command.Description) })
                .ToArray();
            _viewModel.SetSuggestions(commands);
            AnimateSuggestions(commands.Length > 0);
            return;
        }

        var hasCommandPrefix = query.Contains(' ') || query.Contains('/') || query.Contains(':') || query.Contains('\\');
        var suggestions = _discoveredApps
            .Where(app => !hasCommandPrefix && app.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(app => app.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(app => app.Name)
            .Take(8)
            .ToList();

        _viewModel.SetSuggestions(suggestions);
        AnimateSuggestions(query.Length > 0 && suggestions.Count > 0);
    }

    private void AnimateSuggestions(bool show)
    {
        _suggestionsShouldBeVisible = show;
        AppSuggestions.BeginAnimation(UIElement.OpacityProperty, null);
        SuggestionsScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (show)
        {
            AppSuggestions.Visibility = Visibility.Visible;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scale = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            AppSuggestions.BeginAnimation(UIElement.OpacityProperty, fade);
            SuggestionsScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            return;
        }

        if (AppSuggestions.Visibility != Visibility.Visible)
            return;

        var hideFade = new DoubleAnimation(AppSuggestions.Opacity, 0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var hideScale = new DoubleAnimation(SuggestionsScale.ScaleY, 0.85, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        hideFade.Completed += (_, _) =>
        {
            if (!_suggestionsShouldBeVisible && AppSuggestions.Opacity == 0)
                AppSuggestions.Visibility = Visibility.Collapsed;
        };
        AppSuggestions.BeginAnimation(UIElement.OpacityProperty, hideFade);
        SuggestionsScale.BeginAnimation(ScaleTransform.ScaleYProperty, hideScale);
    }

    private void LaunchDiscoveredApp(string query)
    {
        var match = _discoveredApps.FirstOrDefault(app => app.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? _discoveredApps.FirstOrDefault(app => app.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            LaunchApplication(match.Path, match.Name);
            return;
        }

        if (query.Contains('\\') || query.Contains('/') ||
            Path.HasExtension(query))
            LaunchByName(query);
        else
            SearchWeb(query);
    }

    private void LaunchApplication(string path, string displayName)
    {
        try
        {
            if (path.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            TrackAppLaunch(displayName);
            ResultText.Text = TFormat("Opening {0}.", displayName);
        }
        catch
        {
            ResultText.Text = TFormat("Could not open {0}.", displayName);
        }
    }

    private void ShowDiscoveredApps()
    {
        ResultText.Text = _discoveredApps.Count == 0
            ? T("No applications found yet.")
            : TFormat("Found apps: {0}", string.Join(", ", _discoveredApps.OrderBy(app => app.Name).Take(30).Select(app => app.Name)));
    }

    private async void StartApplicationScan()
    {
        if (Interlocked.Exchange(ref _scanInProgress, 1) != 0)
            return;

        DiscoveredAppCountText.Text = T("Scanning apps...");
        try
        {
            var unique = await Task.Run(_appDiscoveryService.Discover);
            _discoveredApps.Clear();
            foreach (var app in unique)
                _discoveredApps.Add(app);
            _applicationScanCompleted = true;
            DiscoveredAppCountText.Text = TFormat("{0} apps discovered", unique.Count);
            RefreshQuickApps();
            QuickAppsEmptyText.Visibility = unique.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            QuickAppsEmptyText.Text = unique.Count == 0
                ? T("No apps available yet.")
                : TFormat("{0} apps available", unique.Count);
            UpdateAppSuggestions();
        }
        catch (Exception exception)
        {
            ResultText.Text = $"Could not scan for apps: {exception.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
        }
    }

    private void AddNote(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ResultText.Text = T("Type a note after the note command.");
            return;
        }

        _notes.Insert(0, $"{DateTime.Now:g} - {text}");
        SaveNotes();
        NotesCountText.Text = FormatNoteCount(_notes.Count);
        ResultText.Text = T("Note saved to your scratchpad.");
        ViewNotes();
    }

    private void ViewNotes_Click(object sender, RoutedEventArgs e) => ViewNotes();

    private void ViewNotes()
    {
        _ = TransitionHistoryViewAsync("notes");
    }

    private async Task TransitionHistoryViewAsync(string view)
    {
        var transitionVersion = ++_historyTransitionVersion;
        var panel = HistoryPanel;
        var transform = (TranslateTransform)panel.RenderTransform;

        panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(panel.Opacity, 0, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(transform.Y, -4, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        });

        await Task.Delay(110);
        if (transitionVersion != _historyTransitionVersion)
            return;

        _historyView = view;
        if (view == "notes")
        {
            ClearClipboardHistoryButton.Visibility = Visibility.Collapsed;
            HistorySectionTitle.Text = T("Notes");
            _viewModel.SetHistory(_notes.Count == 0
                ? [T("No notes yet. Try: /note remember to fix flashlight")]
                : _notes);
            ResultText.Text = _notes.Count == 0 ? T("Scratchpad is empty.") : TFormat("Showing {0} saved note(s).", _notes.Count);
        }
        else
        {
            ClearClipboardHistoryButton.Visibility = Visibility.Visible;
            HistorySectionTitle.Text = T("Clipboard history");
            _viewModel.SetHistory(_clipboardHistory.Count == 0
                ? [T("Copy some text and it will appear here.")]
                : _clipboardHistory);
            ResultText.Text = TFormat("Showing {0} clipboard item(s).", _clipboardHistory.Count);
        }

        panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void SaveNotes()
    {
        try
        {
            _storageService.SaveNotes(_notes);
        }
        catch (Exception exception)
        {
            ResultText.Text = $"Could not save note: {exception.Message}";
        }
    }

    private void LoadNotes()
    {
        try
        {
            var saved = _storageService.LoadNotes();
            if (saved != null) _notes.AddRange(saved);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load notes: {exception}");
        }

        Dispatcher.Invoke(() => NotesCountText.Text = FormatNoteCount(_notes.Count));
    }

    private void StartTimer(string duration)
    {
        var match = Regex.Match(duration, @"^(\d+(?:\.\d+)?)(m|h|s)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            ResultText.Text = T("Timer format: timer 30m, timer 1h, or timer 45s.");
            return;
        }

        var amount = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var multiplier = match.Groups[2].Value.ToLowerInvariant() switch { "h" => 3600, "m" => 60, _ => 1 };
        _countdownEndsAt = DateTime.Now.AddSeconds(amount * multiplier);
        ResultText.Text = TFormat("Countdown started for {0}.", duration);
    }

    private void StartSession(string activity)
    {
        if (string.IsNullOrWhiteSpace(activity)) activity = "Focus session";
        _sessionElapsed = TimeSpan.Zero;
        _sessionStartedAt = DateTime.Now;
        ResultText.Text = TFormat("Session started: {0}", activity);
    }

    private void StopTimer_Click(object sender, RoutedEventArgs e) => StopTimer();

    private void StopTimer()
    {
        _countdownEndsAt = null;
        _sessionElapsed = TimeSpan.Zero;
        _sessionStartedAt = default;
        SessionTimerText.Text = "00:00:00";
        ResultText.Text = T("Timer and activity session stopped.");
    }

    private void TickerTick(object? sender, EventArgs e)
    {
        UpdateClock();

        if (_countdownEndsAt is DateTime countdownEndsAt)
        {
            var remaining = countdownEndsAt - DateTime.Now;
            if (remaining <= TimeSpan.Zero)
            {
                _countdownEndsAt = null;
                SessionTimerText.Text = "00:00:00";
                ResultText.Text = T("Timer finished.");
            }
            else
            {
                SessionTimerText.Text = remaining.ToString("mm\\:ss");
            }
        }
        else
        {
            _sessionElapsed = DateTime.Now - _sessionStartedAt;
            if (_sessionStartedAt != default)
                SessionTimerText.Text = _sessionElapsed.ToString(@"hh\:mm\:ss");
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm:ss");
        CalendarText.Text = now.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo(_isRussian ? "ru-RU" : "en-US"));
    }

    private void CaptureClipboard()
    {
        if (!_clipboardCaptureEnabled)
            return;

        if (_clipboardSequenceBaseline is uint sequenceBaseline)
        {
            if (_clipboardPrivacyService.GetClipboardSequenceNumber() == sequenceBaseline)
                return;

            _clipboardSequenceBaseline = null;
        }

        try
        {
            var text = _clipboardPrivacyService.ReadAllowedText(_excludedClipboardApplications);
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (text == _lastClipboard)
                return;
            _lastClipboard = text;
            _clipboardHistory.Remove(text);
            _clipboardHistory.Insert(0, text);
            if (_clipboardHistory.Count > 20) _clipboardHistory.RemoveAt(20);
            ClipboardCountText.Text = FormatClipboardCount(_clipboardHistory.Count);
        }
        catch (ExternalException exception)
        {
            Debug.WriteLine($"Could not read the clipboard: {exception}");
        }
        catch (InvalidOperationException exception)
        {
            Debug.WriteLine($"Could not read the clipboard: {exception}");
        }
    }

    private void ViewClipboard_Click(object sender, RoutedEventArgs e) => ViewClipboard();

    private void ViewClipboard()
    {
        CaptureClipboard();
        _ = TransitionHistoryViewAsync("clipboard");
    }

    private void Calculate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            ResultText.Text = T("Calculation format: calc 18 * 7");
            return;
        }

        if (!Regex.IsMatch(expression, @"^[\d\s\.+\-*/%()]+$"))
        {
            ResultText.Text = T("Use only numbers and math operators.");
            return;
        }

        try
        {
            var value = new DataTable().Compute(expression.Replace("x", "*", StringComparison.OrdinalIgnoreCase), null);
            ResultText.Text = $"{expression} = {value}";
        }
        catch
        {
            ResultText.Text = T("That calculation could not be evaluated.");
        }
    }

    private void ToggleFocus(string intent)
    {
        Topmost = !Topmost;
        ResultText.Text = Topmost
            ? TFormat("Focus mode on. Intent: {0}", string.IsNullOrWhiteSpace(intent) ? T("Stay on the current task") : intent)
            : T("Focus mode off.");
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/snurrvix")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ResultText.Text = TFormat("Could not open the browser: {0}", exception.Message);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsAnimating)
            return;

        CommandsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.BeginAnimation(UIElement.OpacityProperty, null);
        SettingsPanelOffset.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        SettingsPanel.Visibility = Visibility.Visible;
        SettingsPanel.Opacity = 0;
        SettingsPanelOffset.X = -24;
        SettingsPanelScale.ScaleX = 0.97;
        SettingsPanelScale.ScaleY = 0.97;
        AnimateSettingsPanel(true);
        RefreshStartupButton();
    }

    private void CommandsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_commandsAnimating)
            return;

        SettingsPanel.Visibility = Visibility.Collapsed;
        if (CommandsPanel.Visibility == Visibility.Visible)
        {
            AnimateCommandsPanel(false);
            return;
        }

        RefreshCommandsList();
        CommandsPanel.Visibility = Visibility.Visible;
        AnimateCommandsPanel(true);
    }

    private void CommandsCloseButton_Click(object sender, RoutedEventArgs e)
    {
        AnimateCommandsPanel(false);
    }

    private void AnimateCommandsPanel(bool open)
    {
        CommandsPanel.BeginAnimation(UIElement.OpacityProperty, null);
        CommandsPanelOffset.BeginAnimation(TranslateTransform.XProperty, null);
        CommandsPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CommandsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (open)
        {
            CommandsPanel.Opacity = 0;
            CommandsPanelOffset.X = -24;
            CommandsPanelScale.ScaleX = 0.97;
            CommandsPanelScale.ScaleY = 0.97;
        }

        _commandsAnimating = true;
        var duration = TimeSpan.FromMilliseconds(open ? 260 : 190);
        var easing = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        var opacity = new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, duration) { EasingFunction = easing };
        var offset = new DoubleAnimation(open ? -24 : 0, open ? 0 : -24, duration) { EasingFunction = easing };
        var scaleX = new DoubleAnimation(open ? 0.97 : 1, open ? 1 : 0.97, duration) { EasingFunction = easing };
        var scaleY = new DoubleAnimation(open ? 0.97 : 1, open ? 1 : 0.97, duration) { EasingFunction = easing };

        opacity.Completed += (_, _) =>
        {
            if (!open)
                CommandsPanel.Visibility = Visibility.Collapsed;
            _commandsAnimating = false;
        };

        CommandsPanel.BeginAnimation(UIElement.OpacityProperty, opacity);
        CommandsPanelOffset.BeginAnimation(TranslateTransform.XProperty, offset);
        CommandsPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        CommandsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void SettingsCloseButton_Click(object sender, RoutedEventArgs e)
    {
        ThemePopup.IsOpen = false;
        if (SettingsPanel.Visibility != Visibility.Visible)
            return;

        SettingsPanel.BeginAnimation(UIElement.OpacityProperty, null);
        SettingsPanelOffset.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        SettingsPanel.Opacity = 1;
        SettingsPanelOffset.X = 0;
        SettingsPanelScale.ScaleX = 1;
        SettingsPanelScale.ScaleY = 1;
        AnimateSettingsPanel(false);
    }

    private void AnimateSettingsPanel(bool open)
    {
        SettingsPanel.BeginAnimation(UIElement.OpacityProperty, null);
        SettingsPanelOffset.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (open)
        {
            SettingsPanel.Opacity = 0;
            SettingsPanelOffset.X = -24;
            SettingsPanelScale.ScaleX = 0.97;
            SettingsPanelScale.ScaleY = 0.97;
        }

        _settingsAnimating = true;
        var easing = new CubicEase
        {
            EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn
        };
        var duration = TimeSpan.FromMilliseconds(open ? 260 : 190);
        var opacity = new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, duration)
        {
            EasingFunction = easing
        };
        var offset = new DoubleAnimation(open ? -24 : 0, open ? 0 : -24, duration)
        {
            EasingFunction = easing
        };
        var scaleX = new DoubleAnimation(open ? 0.97 : 1, open ? 1 : 0.97, duration)
        {
            EasingFunction = easing
        };
        var scaleY = new DoubleAnimation(open ? 0.97 : 1, open ? 1 : 0.97, duration)
        {
            EasingFunction = easing
        };

        if (!open)
        {
            opacity.Completed += (_, _) =>
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                _settingsAnimating = false;
            };
        }
        else
        {
            opacity.Completed += (_, _) => _settingsAnimating = false;
        }

        SettingsPanel.BeginAnimation(UIElement.OpacityProperty, opacity);
        SettingsPanelOffset.BeginAnimation(TranslateTransform.XProperty, offset);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void StartupButton_Click(object sender, RoutedEventArgs e)
    {
        ConfigureStartup(IsStartupEnabled() ? "off" : "on");
        RefreshStartupButton();
    }

    private bool IsStartupEnabled()
        => _startupService.IsEnabled();

    private void RefreshStartupButton()
    {
        StartupButton.Content = IsStartupEnabled()
            ? (_isRussian ? "Disable at startup" : "Disable at startup")
            : (_isRussian ? "Enable at startup" : "Enable at startup");
    }

    private void ThemeMenuButton_Click(object sender, RoutedEventArgs e)
    {
        ThemePopup.IsOpen = !ThemePopup.IsOpen;
    }

    private void ThemePopup_Opened(object sender, EventArgs e)
    {
        CacheFontSizeBaselines(ThemePopupSurface);
        ApplyResponsiveFontSizes(ThemePopupSurface, GetResponsiveFontScale());
        ThemePopupSurface.BeginAnimation(UIElement.OpacityProperty, null);
        ThemePopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ThemePopupSurface.Opacity = 0;
        ThemePopupScale.ScaleY = 0.85;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scale = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
        };
        ThemePopupSurface.BeginAnimation(UIElement.OpacityProperty, fade);
        ThemePopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void ThemePopup_Closed(object sender, EventArgs e)
    {
        ThemePopupSurface.BeginAnimation(UIElement.OpacityProperty, null);
        ThemePopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ThemePopupSurface.Opacity = 0;
        ThemePopupScale.ScaleY = 0.85;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
            return;

        var theme = button.Tag?.ToString() ?? "Midnight";
        ThemeMenuButton.Content = $"{theme}  ▾";
        ThemePopup.IsOpen = false;
        ApplyTheme(theme);
        SaveThemePreference(theme);
    }

    private void ApplyTheme(string theme)
    {
        var colors = theme switch
        {
            "Ocean" => new[] { "#D90D3B66", "#D8127C8C", "#D938BDF0", "#D91A4A78", "#D91B263B" },
            "Sunset" => new[] { "#D9431C5C", "#D9C44569", "#D9F08A5D", "#D9A84E9A", "#D9272D4D" },
            "Forest" => new[] { "#D918382D", "#D9226B57", "#D941A36C", "#D91F6F5E", "#D91B2A24" },
            "Aurora" => new[] { "#D9182A4A", "#D94F3D8B", "#D9228A91", "#D95B8C7A", "#D926416A" },
            "Graphite" => new[] { "#D9252729", "#D93D414D", "#D9676A73", "#D943474F", "#D91C1C1F" },
            _ => new[] { "#D91C244A", "#D9321F5C", "#D91A465C", "#D9402A69", "#D9222942" }
        };

        AnimateGradientColor(GradientColor1, colors[0]);
        AnimateGradientColor(GradientColor2, colors[1]);
        AnimateGradientColor(GradientColor3, colors[2]);
        AnimateGradientColor(GradientColor4, colors[3]);
        AnimateGradientColor(GradientColor5, colors[4]);
        AnimateBrandGradient(colors);
    }

    private static void AnimateGradientColor(GradientStop gradientStop, string hexColor)
    {
        var color = (Color)ColorConverter.ConvertFromString(hexColor);
        gradientStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation
        {
            To = color,
            Duration = TimeSpan.FromMilliseconds(650),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void AnimateBrandGradient(string[] colors)
    {
        AnimateGradientColor(BrandGradient1, colors[0]);
        AnimateGradientColor(BrandGradient2, colors[1]);
        AnimateGradientColor(BrandGradient3, colors[2]);
        AnimateGradientColor(BrandGradient4, colors[3]);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private static void LogStartupFailure(Exception exception)
    {
        Debug.WriteLine($"FlowDeck startup log could not be written: {exception}");
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAnimating)
        {
            _allowWindowClose = true;
            AnimateClose(Application.Current.Shutdown);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}