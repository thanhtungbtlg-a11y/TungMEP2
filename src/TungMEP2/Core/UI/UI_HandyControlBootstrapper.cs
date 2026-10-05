using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace VP_RevitAPI.UI;

public static class UI_HandyControlBootstrapper
{
	private static readonly Uri SkinDefaultUri = new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml", UriKind.Absolute);

	private static readonly Uri ThemeUri = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml", UriKind.Absolute);

	public static void EnsureInitialized()
	{
		System.Windows.Application application = System.Windows.Application.Current;
		if (application != null)
		{
			Collection<ResourceDictionary> mergedDictionaries = application.Resources.MergedDictionaries;
			if (!mergedDictionaries.Any((ResourceDictionary d) => d.Source != null && d.Source.Equals(SkinDefaultUri)))
			{
				mergedDictionaries.Add(new ResourceDictionary
				{
					Source = SkinDefaultUri
				});
			}
			if (!mergedDictionaries.Any((ResourceDictionary d) => d.Source != null && d.Source.Equals(ThemeUri)))
			{
				mergedDictionaries.Add(new ResourceDictionary
				{
					Source = ThemeUri
				});
			}
		}
	}
}
