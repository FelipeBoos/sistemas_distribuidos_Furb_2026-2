# Dockerfile generico para qualquer um dos servicos de aplicacao do TP02 - o nome do projeto
# entra via build arg, evitando duplicar o mesmo Dockerfile 11 vezes (um container por servico,
# mas um unico Dockerfile como padrao, conforme docs/guia-apresentacao.md).
ARG PROJECT_NAME

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG PROJECT_NAME
WORKDIR /src
COPY . .
RUN dotnet publish "${PROJECT_NAME}/${PROJECT_NAME}.csproj" -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
ARG PROJECT_NAME
ENV PROJECT_DLL=${PROJECT_NAME}.dll
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet \"$PROJECT_DLL\" \"$@\"", "--"]
