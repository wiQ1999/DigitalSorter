using DigitalSorter.Enums;
using System.Collections.Generic;

namespace DigitalSorter.Services;

public interface IDigitalMetadataReader
{
    void LoadDigitSource(string path);

    Dictionary<NamingType, string> ReadMetadata();
}
