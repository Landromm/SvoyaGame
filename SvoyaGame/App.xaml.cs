using Microsoft.Extensions.DependencyInjection;
using SvoyaGame.Services;
using SvoyaGame.Services.Implementations;
using SvoyaGame.View.MainViews;
using SvoyaGame.ViewModel.MainViewModel;
using System.Configuration;
using System.Data;
using System.Windows;
using static System.Formats.Asn1.AsnWriter;

namespace SvoyaGame;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
	private static IServiceProvider? _servicesProvider;

	public static IServiceProvider Services => _servicesProvider ??= InitializeServices().BuildServiceProvider();

	private static IServiceCollection InitializeServices()
	{
		var services = new ServiceCollection();

		services.AddSingleton<MainViewModel>();
		services.AddScoped<SettingsViewModel>();

		services.AddSingleton<IUserDialog, UserDialogServices>();

		services.AddTransient(
			s =>
			{
				var model = s.GetRequiredService<MainViewModel>();
				var window = new MainWindow { DataContext = model };
				model.DialogComplete += (_, _) => window.Close();

				return window;
			});
		services.AddTransient(
			s =>
			{
				var scope = s.CreateScope();
				var model = s.GetRequiredService<SettingsViewModel>();
				var window = new SettingsWindow { DataContext = model };
				model.DialogComplete += (_, _) => window.Close();
				window.Closed += (_, _) => scope.Dispose();

				return window;
			});

		return services;
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		Services.GetRequiredService<IUserDialog>().OpenMainWindow();
	}
}
