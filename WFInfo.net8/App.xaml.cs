﻿using System.Windows;

namespace WFInfo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private void Application_Exit(object sender, ExitEventArgs e)
    {
        if (WFInfo.MainWindow.INSTANCE != null)
        {
            WFInfo.Main.INSTANCE.DisposeTesseract();
            WFInfo.MainWindow.listener.Dispose();
            WFInfo.MainWindow.INSTANCE.Exit(null, null);
        }
    }
}