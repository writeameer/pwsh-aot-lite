FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGET_RID=linux-arm64
WORKDIR /src

RUN apt-get update -qq \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends clang git zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

COPY . .
RUN set -eux; \
    . /src/eng/upstream-powershell.env; \
    git clone --filter=blob:none --no-checkout "$PowerShellUpstreamRepository" /src/.upstream/PowerShell; \
    git -C /src/.upstream/PowerShell fetch --depth 1 origin "$PowerShellUpstreamCommit"; \
    git -C /src/.upstream/PowerShell checkout --detach "$PowerShellUpstreamCommit"; \
    dotnet publish -c Release -r ${TARGET_RID} --self-contained true -o /out

# Keep the SDK base for this spike so the image is known to carry every native
# dependency required by the AOT executable. Slimming this is a packaging task,
# not part of the execution-model proof.
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=build /out /app
COPY --from=build /src/extensions /app/extensions
COPY --from=build /src/repositories /app/repositories
WORKDIR /app
ENTRYPOINT ["/app/PwshAotLite"]
