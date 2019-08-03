﻿using System.Diagnostics;
using FluentMusic.Core.Extensions;
using FluentMusic.Views;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using FluentMusic.Core;

namespace FluentMusic
{
    sealed partial class App : Application
    {
        private Frame _rootFrame;

        public App()
        {
            InitializeComponent();

            UnhandledException += App_UnhandledException;
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Debugger.Break();
        }

        protected override async void OnWindowCreated(WindowCreatedEventArgs args)
        {
            base.OnWindowCreated(args);

            //await ServiceFacade.IndexService.BeginIndexAsync();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            base.OnLaunched(e);

            await ServiceFacade.StartupAsync();
            await ViewModelAccessor.StartupAsync();
            await ServiceFacade.IndexService.BeginIndexAsync();
            if (e.Kind == ActivationKind.Launch)
            {
                if (Window.Current.Content == null)
                {
                    CreateRootFrame();
                }

                if (_rootFrame.Content == null)
                {
                    NavigateStartupPage();
                }

                Window.Current.Activate();
            }
            else
            {
                // TODO
            }
        }

        private void NavigateStartupPage()
        {
            var contains = Static.LocalSettings.ContainsKey(Static.Settings.FirstRun);

            // TODO: disable welcome page while developing
            if (!contains)
            {
                _rootFrame.Navigate(typeof(Shell));
                _rootFrame = _rootFrame.Content.Cast<Page>().FindName("ShellFrame").Cast<Frame>();
                _rootFrame.Navigate(typeof(FullPlayerPage));
            }
            else
            {
                SeedSettings();
                _rootFrame.Navigate(typeof(WelcomePage));
            }
        }

        private void SeedSettings()
        {
            Static.LocalSettings[Static.Settings.Behavior.AutoScroll] = true;
        }

        private void CreateRootFrame()
        {
            _rootFrame = Window.Current.Content as Frame;

            if (_rootFrame == null)
            {
                _rootFrame = new Frame();

                Window.Current.Content = _rootFrame;
            }

            ExtendView();
        }

        private static void ExtendView()
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }
}