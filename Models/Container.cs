using DigitalSorter.Enums;
using System.Collections.Generic;
using System.IO;

namespace DigitalSorter.Models
{
    public class Container
    {
        private readonly List<string> _digitalSourcePaths;
        private string _targetLocation;

        public IReadOnlyList<string> DigitalSourcePaths => _digitalSourcePaths;

        public NamingType RenameOption { get; set; }

        public string TargetLocation
        {
            get => _targetLocation;
            set
            {
                _targetLocation = value;
                ThrowIfInvalidLocation();
            }
        }

        public Container()
        {
            _digitalSourcePaths = new List<string>();
            _targetLocation = default!;
            RenameOption = default!;
        }

        private void ThrowIfInvalidLocation()
        {
            if (string.IsNullOrEmpty(_targetLocation))
                return;

            var isExist = Directory.Exists(_targetLocation);

            if (!isExist)
                throw new DirectoryNotFoundException(_targetLocation);
        }

        public int AddDigitalSourcePaths(IEnumerable<string> digitalSourcePaths)
        {
            var inserted = 0;

            foreach (var digitalSourcePath in digitalSourcePaths)
            {
                if (!digitalSourcePath.Contains(TargetLocation))
                {
                    _digitalSourcePaths.Add(digitalSourcePath);
                    inserted++;
                }
            }

            return inserted;
        }

        public int RemoveDigitalSourcePaths(IEnumerable<string> digitalSourcePaths)
        {
            var removed = 0;

            foreach (var digitalSourcePath in digitalSourcePaths)
            {
                var isRemoved = _digitalSourcePaths.Remove(digitalSourcePath);

                if (isRemoved)
                    removed++;
            }

            return removed;
        }
    }
}
