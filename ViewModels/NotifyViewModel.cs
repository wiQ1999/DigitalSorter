﻿using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DigitalSorter.ViewModels;

public class NotifyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnChangedChanged(
        [CallerMemberName] string propName = null!)
    {
        PropertyChanged?.Invoke(
            this, new PropertyChangedEventArgs(propName));
    }
}
