namespace VP_RevitAPI.Helper.Sheets;

public sealed class PrinterOption
{
	public string Name { get; }

	public string? DriverName { get; }

	public bool IsActive { get; }

	public bool IsNativePdf { get; }

	public PrinterOption(
		string name,
		string? driverName,
		bool isActive,
		bool isNativePdf)
	{
		Name = name;
		DriverName = driverName;
		IsActive = isActive;
		IsNativePdf = isNativePdf;
	}

	public static PrinterOption CreateNativePdf()
	{
		return new PrinterOption(
			"Revit Native PDF",
			null,
			isActive: true,
			isNativePdf: true);
	}

	public static PrinterOption CreateVirtualPrinter(string driverName, bool isActive)
	{
		return new PrinterOption(
			driverName,
			driverName,
			isActive,
			isNativePdf: false);
	}
}
