﻿using System;
using ReactiveUI;
using SextantSample.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace SextantSample.UWP.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class FirstModalView : Page, IViewFor<FirstModalViewModel>
    {
        public FirstModalView()
        {
            this.InitializeComponent();
                        
            Interactions
                .ErrorMessage
                .RegisterHandler(async x =>
                {
                    var dialog = new Windows.UI.Popups.MessageDialog(x.Input.Message, "Error");
                    dialog.Commands.Add(new Windows.UI.Popups.UICommand("Done"));
                    _ = await dialog.ShowAsync();
                    x.SetOutput(true);
                });
        }

        public static readonly DependencyProperty ViewModelProperty = DependencyProperty
           .Register(nameof(ViewModel), typeof(FirstModalViewModel), typeof(FirstModalView), null);

        public FirstModalViewModel ViewModel
        {
            get => (FirstModalViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        object IViewFor.ViewModel { get => ViewModel; set => ViewModel = (FirstModalViewModel)value; }
    }
}
