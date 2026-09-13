using System.Windows;
using System.Windows.Input;

namespace Akinti.Ui;

/// <summary>
/// Sifreli arsiv icin sifre sorar.
///
/// Sifre yalnizca bu pencerede yasar: hicbir yere yazilmaz, kaydedilmez ve
/// arsivciye komut satiri argumani olarak verilir (sureç bittiginde gider).
/// </summary>
public partial class PasswordWindow : Window
{
    public string Password => TxtPassword.Password;

    /// <param name="archiveName">Kullanicinin hangi dosya icin sifre girdigini gormesi icin.</param>
    /// <param name="wrongPassword">Bir onceki deneme yanlissa uyari gosterilir.</param>
    public PasswordWindow(string archiveName, bool wrongPassword = false)
    {
        InitializeComponent();
        WindowEffects.Attach(this);

        TxtName.Text = archiveName;
        if (wrongPassword) TxtError.Visibility = Visibility.Visible;

        Loaded += (_, _) => TxtPassword.Focus();
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Accept();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => Accept();

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        if (TxtPassword.Password.Length == 0) return;
        DialogResult = true;
    }
}
