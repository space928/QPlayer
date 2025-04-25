## QPlayer Icons

Most of QPlayers icons come from [FontAwesome](), to add new icons to QPlayer, they must be 
converted from an SVG to XAML. This can done by using the `Update.ps1` script which converts
all `*.svg` files in the directory into a single `ConvertedIcons.xaml` file. To use the 
converted icon, make sure to add a reference to it in `QPlayer/ThemesV2/Icons.xaml`. Please
also note that the `Update.ps1` script relies on 
[SvgToXaml](https://github.com/BerndK/SvgToXaml/releases/tag/Ver_1.3.0), the executable for
which must be placed in `Qplayer/Resources/Icons/SvgToXaml/SvgToXaml.exe`.


