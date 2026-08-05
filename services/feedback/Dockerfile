# Exports unitary/toxic-bert (Apache-2.0) to ONNX from the original weights. A heavy stage that
# contributes nothing but two files to the final image: the alternative is trusting a re-upload of
# someone else's export, which is not a thing to put in front of the public.
FROM python:3.12-slim AS model
WORKDIR /export
RUN pip install --no-cache-dir "optimum[exporters]==1.24.0" "torch==2.5.1" --extra-index-url https://download.pytorch.org/whl/cpu
RUN optimum-cli export onnx \
        --model unitary/toxic-bert \
        --task text-classification \
        /export/out
RUN test -f /export/out/model.onnx && test -f /export/out/vocab.txt

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Feedback.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

# Not alpine: ONNX Runtime's native library wants glibc, and the musl build is a separate package.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=model /export/out/model.onnx /app/model/model.onnx
COPY --from=model /export/out/vocab.txt /app/model/vocab.txt

# Not root: the only thing here worth taking is the GitHub token in the environment.
RUN useradd --uid 10001 --create-home svc
USER svc

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV CLASSIFIER_DIR=/app/model

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Feedback.dll"]
