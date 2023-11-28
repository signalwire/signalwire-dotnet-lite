export DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0
rm -rf *.nupkg
if [ ! -d "laml" ]; then
    echo "Missing laml directory, run bootstrap.sh"
    exit
fi

dotnet pack -c Release -o .
dotnet nuget push `ls *.nupkg` -k $NUGET_APIKEY -s https://api.nuget.org/v3/index.json
