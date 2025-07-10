FROM mcr.microsoft.com/dotnet/sdk:9.0

USER root

# Create non-root vscode user
RUN adduser --disabled-password --gecos "" vscode

# Install minimal dependencies and PowerShell from Microsoft repo
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    curl \
    git \
    ca-certificates && \
    curl -sSL https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb -o packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y --no-install-recommends powershell && \
    rm -rf /var/lib/apt/lists/*

# Install Node.js 20 and Claude Code
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get update && \
    apt-get install -y --no-install-recommends nodejs && \
    npm install -g @anthropic-ai/claude-code && \
    rm -rf /var/lib/apt/lists/*

# Set up PowerShell profiles
COPY ./scripts/*.ps1 /home/vscode/
RUN mkdir -p /home/vscode/.config/powershell && \
    cp /home/vscode/profile.ps1 /home/vscode/.config/powershell/Microsoft.PowerShell_profile.ps1 && \
    cp /home/vscode/profile.ps1 /home/vscode/.config/powershell/Microsoft.dotnet-interactive_profile.ps1 && \
    cp /home/vscode/profile.ps1 /home/vscode/.config/powershell/Microsoft.VSCode_profile.ps1 && \
    chmod +x /home/vscode/*.ps1 && \
    chown -R vscode:vscode /home/vscode

USER vscode
WORKDIR /home/vscode

CMD ["sleep", "infinity"]
