FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY MatrixMultiply.sln ./
COPY src/MatrixMultiply.Core/ src/MatrixMultiply.Core/
COPY src/MatrixMultiply.Cli/ src/MatrixMultiply.Cli/
COPY tests/MatrixMultiply.Tests/ tests/MatrixMultiply.Tests/
RUN dotnet restore MatrixMultiply.sln
RUN dotnet publish src/MatrixMultiply.Cli/MatrixMultiply.Cli.csproj -c Release -o /app --no-restore

FROM build AS test
RUN dotnet test tests/MatrixMultiply.Tests/MatrixMultiply.Tests.csproj -c Release --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MatrixMultiply.Cli.dll"]
