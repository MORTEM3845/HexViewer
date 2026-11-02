namespace HexViewer.Items.Enums;

/// <summary>Byte significance when interpreting a numeric word.</summary>
public enum HexByteOrder { LittleEndian, BigEndian }

/// <summary>Visual order only; never changes the underlying data.</summary>
public enum HexByteDisplayOrder { AddressOrder, ReverseWithinWord }
