sc stop jsdal-server


@echo Building web component
@echo ----------------------------------------
npm --prefix "../../web" run publish & dotnet publish --configuration Release --output ./00-Release & sc start jsdal-server