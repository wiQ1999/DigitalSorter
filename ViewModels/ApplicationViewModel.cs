using DigitalSorter.Enums;
using DigitalSorter.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DigitalSorter.ViewModels;

public class ApplicationViewModel : NotifyViewModel
{
    private readonly Container _container;

    public IEnumerable<string> Paths => _container.DigitalSourcePaths;

    public ObservableCollection<NamingType> RenameOptions { get; }

    public NamingType SelectedRenameOption
    {
        get => _container.RenameOption;
        set
        {
            _container.RenameOption = value;
            OnPropertyChanged();
        }
    }

    public string TargetLocation
    {
        get => _container.TargetLocation;
        set
        {
            _container.TargetLocation = value;
            OnPropertyChanged();
        }
    }

    public ApplicationViewModel()
    {
        _container = new Container()
        {
            RenameOption = NamingType.None,
            TargetLocation = string.Empty
        };

        _container.AddDigitalSourcePaths(new string[] { "path01", "path02", "path03" });

        var renameOptionValues = Enum
            .GetValues<NamingType>()
            .SkipWhile(nt => nt == NamingType.None);

        RenameOptions = new ObservableCollection<NamingType>(renameOptionValues);
    }
}
