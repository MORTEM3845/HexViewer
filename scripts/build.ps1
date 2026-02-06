Param ([string]
$BuildId,
$BuildSourcesDirectory,
$Csproj)
###
$solutionPath = "$BuildSourcesDirectory\$Csproj`_Release.sln"
$Msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\msbuild.exe"
$sn = "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\sn.exe"
#
& $sn "-k" "$BuildSourcesDirectory\$Csproj\key.snk"
#
$xml = [xml](Get-Content "$BuildSourcesDirectory\$Csproj\$Csproj`_Release.csproj")
$xml.Project.PropertyGroup[0].Version = "1.0.$BuildId"
$child = $xml.CreateElement("AssemblyOriginatorKeyFile")
$child.InnerText = "key.snk"
$xml.Project.PropertyGroup[0].AppendChild($child)
$child = $xml.CreateElement("SignAssembly")
$child.InnerText = "true"
$xml.Project.PropertyGroup[0].AppendChild($child)
$xml.Save("$BuildSourcesDirectory\$Csproj\$Csproj`_Release.csproj")
#
Set-Execute -executePath $Msbuild -arguments @("$solutionPath" , "-property:platform=Any Cpu;configuration=release")
