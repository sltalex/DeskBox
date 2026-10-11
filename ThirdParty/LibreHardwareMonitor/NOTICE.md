# Temperature probe third-party notices

DeskBox's optional temperature probe uses `LibreHardwareMonitorLib` 0.9.6,
licensed under the Mozilla Public License 2.0 (MPL-2.0). The source for the
exact NuGet package is available from
<https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701>.
The full license text is included as `MPL-2.0.txt` and is also available at
<https://www.mozilla.org/en-US/MPL/2.0/>.

The probe is a separate, self-contained .NET process and is launched only while
a System Monitor widget is visible. The DeskBox main executable remains Native
AOT; the whole installation is not purely Native AOT.

The package's runtime dependencies are shipped unchanged by NuGet. Their source,
license and package metadata can be obtained from the following package pages:

- DiskInfoToolkit 1.1.2: <https://www.nuget.org/packages/DiskInfoToolkit/1.1.2>
- BlackSharp.Core 1.0.7: <https://www.nuget.org/packages/BlackSharp.Core/1.0.7>
- HidSharp 2.6.4: <https://www.nuget.org/packages/HidSharp/2.6.4>
- RAMSPDToolkit-NDD 1.4.2: <https://www.nuget.org/packages/RAMSPDToolkit-NDD/1.4.2>
- Mono.Posix.NETStandard 1.0.0: <https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0>
- System.IO.Ports 10.0.3: <https://www.nuget.org/packages/System.IO.Ports/10.0.3>
- System.Management 10.0.2: <https://www.nuget.org/packages/System.Management/10.0.2>
- System.CodeDom 10.0.2: <https://www.nuget.org/packages/System.CodeDom/10.0.2>
- System.IO.FileSystem.AccessControl 5.0.0: <https://www.nuget.org/packages/System.IO.FileSystem.AccessControl/5.0.0>
- Microsoft .NET runtime and framework components: <https://github.com/dotnet/runtime>

DiskInfoToolkit, BlackSharp.Core and RAMSPDToolkit-NDD declare MPL-2.0.
The .NET packages above declare MIT. HidSharp's complete package license is
included at `TemperatureProbe/licenses/HidSharp-LICENSE.txt`.
