FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGET_RID=linux-arm64
WORKDIR /src

RUN apt-get update -qq \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

COPY . .
RUN dotnet publish -c Release -r ${TARGET_RID} --self-contained true -o /out

# Keep the SDK base for this spike so the image is known to carry every native
# dependency required by the AOT executable. Slimming this is a packaging task,
# not part of the execution-model proof.
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=build /out /app
ENTRYPOINT ["/app/PwshAotLite"]
