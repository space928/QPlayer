# SvgToXaml somehow, for some reason writes to standard *input* when it exits. So we have to do this junk so that it doesn't break the rest of the script...
# Start-Process "SvgToXaml\SvgToXaml.exe" -ArgumentList 'BuildDict /inputdir:. /outputdir:. /outputname ConvertedIcons /nameprefix "Icon" /buildhtmlfile=false' -Wait -UseNewEnvironment
&".\SvgToXaml\SvgToXaml.exe" BuildDict /inputdir:. /outputdir:. /outputname ConvertedIcons /nameprefix "Icon" /buildhtmlfile=false
Read-Host

(Get-Content .\ConvertedIcons.xaml) -replace 'Brush=\"#.*?\"','Brush="{StaticResource IconColor}"' | Out-File .\ConvertedIcons.xaml
Write-Output "Updated icons!"
