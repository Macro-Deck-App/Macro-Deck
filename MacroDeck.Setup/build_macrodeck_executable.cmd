dotnet restore "..\MacroDeck\MacroDeck.csproj"
dotnet build "..\MacroDeck\MacroDeck.csproj" -c Release -o "..\MacroDeck\bin\Release" -r win-x64
dotnet publish "..\MacroDeck\MacroDeck.csproj" -c Release -o "..\MacroDeck\bin\Publish" -r win-x64 --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true
copy "..\MacroDeck\bin\Release\Macro Deck 2.dll" "..\MacroDeck\bin\Publish"