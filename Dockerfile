FROM mcr.microsoft.com/dotnet/runtime:6.0

# Set the working directory inside the container
WORKDIR /app

# Copy the published application into the container
COPY . .

# Install sqlcmd and dependencies. This installs the Microsoft ODBC driver
# and the sqlcmd tool so that migration scripts containing GO batch
# separators can be executed correctly.
RUN apt-get update && \
    apt-get install -y curl apt-transport-https gnupg && \
    curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    curl https://packages.microsoft.com/config/ubuntu/20.04/prod.list > /etc/apt/sources.list.d/mssql-release.list && \
    apt-get update && \
    ACCEPT_EULA=Y apt-get install -y msodbcsql17 mssql-tools unixodbc-dev && \
    # Add sqlcmd to PATH for all users
    echo 'export PATH="$PATH:/opt/mssql-tools/bin"' >> /etc/bash.bashrc

# Define the entrypoint for the container. When the container starts it
# will run the MigrationRunner.dll, which scans for and applies SQL
# migration scripts.
ENTRYPOINT ["dotnet", "MigrationRunner.dll"]