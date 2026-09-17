using DigitalSorter.Enums;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalSorter.Services
{
    public class DigitalMetadataReader 
        : IDigitalMetadataReader
    {
        public string Path { get; private set; } = default!;

        public void LoadDigitSource(string path)
        {
            Path = path;
        }

        public Dictionary<NamingType, string> ReadMetadata()
        {
            var dictionary = new Dictionary<NamingType, string>();

            var directories = ImageMetadataReader.ReadMetadata(Path);

            var subIfdDirectory = directories
                .OfType<ExifSubIfdDirectory>()
                .FirstOrDefault();

            var createdDateTimeText = subIfdDirectory?
                .GetDescription(ExifDirectoryBase.TagDateTimeOriginal);

            if (createdDateTimeText != null)
            {
                var isParsed = DateTime.TryParse(
                    createdDateTimeText, out DateTime createdDateTime);

                if (isParsed)
                {
                    dictionary.Add(NamingType.Date, createdDateTime.ToLongDateString());
                    dictionary.Add(NamingType.Time, createdDateTime.ToLongTimeString());
                }
            }

            // TODO pozostałe typy nazw

            return dictionary;
        }
    }
}
