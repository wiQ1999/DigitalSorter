namespace DigitalSorter.Enums;

public enum NamingType
{
    None = 0,
    Date = 1,
    Time = 1 << 1,
    Location = 1 << 2,
    DateTime = 1 << 3,
    DateTimeLocation = 1 << 4
}
