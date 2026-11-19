using System.Windows;
using Finpulse.Application;
using Finpulse.Infrastructure;

namespace Finpulse.Desktop;

public partial class MainWindow : Window
{
    private readonly AuthService? _auth;
    private readonly StorageConfiguration? _configuration;
    private readonly SqliteStore? _local;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            _configuration = StorageConfiguration.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            if (_configuration.Provider == "SQLite") _local = _configuration.CreateLocal();
            _auth = new AuthService(_local ?? _configuration.CreateUsers(), new Pbkdf2PasswordHasher());
            ConnectionInfo.Text = $"Proveedor: {_configuration.Provider}. Crea tu cuenta o inicia sesión.";
        }
        catch (Exception)
        {
            ConnectionInfo.Text = "No se pudo preparar el almacenamiento. Revisa appsettings.json, el proveedor y los permisos de la carpeta local.";
            LoginButton.IsEnabled = false;
            RegisterButton.IsEnabled = false;
            return;
        }
    }

    private void GoRegister_Click(object sender, RoutedEventArgs e) => ShowScreen("register");
    private void GoLogin_Click(object sender, RoutedEventArgs e) => ShowScreen("login");
    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        LoginPassword.Clear();
        RegisterPassword.Clear();
        ConfirmPassword.Clear();
        ShowScreen("login");
        StatusText.Text = "Sesión cerrada.";
    }

    private void ShowScreen(string screen)
    {
        LoginPanel.Visibility = screen == "login" ? Visibility.Visible : Visibility.Collapsed;
        RegisterPanel.Visibility = screen == "register" ? Visibility.Visible : Visibility.Collapsed;
        DashboardPanel.Visibility = screen == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "";
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (_auth is null) return;
        RegisterButton.IsEnabled = false;
        StatusText.Text = "Registrando cuenta...";
        try
        {
            var name = RegisterFullNameTextBox.Text;
            var email = RegisterEmail.Text;
            var password = RegisterPassword.Password;
            var confirm = ConfirmPassword.Password;
            var result = await Task.Run(() => _auth.RegisterAsync(name, email, password, confirm));
            RegisterPassword.Clear();
            ConfirmPassword.Clear();
            if (result.Exitoso)
            {
                LoginEmail.Text = RegisterEmail.Text;
                ShowScreen("login");
            }
            StatusText.Text = result.Mensaje;
        }
        catch (Exception)
        {
            StatusText.Text = "No fue posible completar el registro. Revisa el almacenamiento del proveedor seleccionado.";
        }
        finally { RegisterButton.IsEnabled = true; }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_auth is null) return;
        LoginButton.IsEnabled = false;
        StatusText.Text = "Verificando credenciales...";
        try
        {
            var email = LoginEmail.Text;
            var password = LoginPassword.Password;
            var result = await Task.Run(() => _auth.LoginAsync(email, password));
            LoginPassword.Clear();
            if (result.Exitoso && result.Usuario is not null)
            {
                if (_local is not null && _configuration is not null)
                {
                    var workspace = new WorkspaceWindow(_local, result.Usuario, _configuration, _local.DatabasePath);
                    workspace.Closed += (_, _) => { Show(); ShowScreen("login"); StatusText.Text = "Sesión cerrada."; };
                    workspace.Show(); Hide();
                    return;
                }
                WelcomeName.Text = result.Usuario.NombreCompleto;
                WelcomeEmail.Text = result.Usuario.Correo;
                ShowScreen("dashboard");
            }
            else StatusText.Text = result.Mensaje;
        }
        catch (Exception)
        {
            StatusText.Text = "No se pudo iniciar sesión. Comprueba el almacenamiento del proveedor seleccionado.";
        }
        finally { LoginButton.IsEnabled = true; }
    }
}
