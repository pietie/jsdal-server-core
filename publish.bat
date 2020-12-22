sc stop jsdal-server


@echo Building web component
@echo ----------------------------------------
npm --prefix "../../web" run publish & dotnet publish --configuration Release --output ./00-Release & sc start jsdal-server


@echo dotnet publish -r win-x64 -c Release --output "./00-Release" /p:PublishSingleFile=true


30845



dotnet publish --configuration Debug --output ./00-Release