FROM flyway/flyway:13.7.0

# The upstream image bundles drivers for every supported database. This image
# only migrates SQL Server, so remove the unused Databricks driver (and its
# vulnerable transitive HTTP components) and pin the Microsoft driver version
# used by Azure SQL. The checksum keeps the downloaded driver reproducible.
ARG MSSQL_JDBC_VERSION=13.6.0.jre11
ARG MSSQL_JDBC_SHA256=4c566e4022c4dcf1489aeb639fe49a0f6ffae3d796fdc2f56b5b8882d4c3bf5a

ADD https://repo1.maven.org/maven2/com/microsoft/sqlserver/mssql-jdbc/${MSSQL_JDBC_VERSION}/mssql-jdbc-${MSSQL_JDBC_VERSION}.jar /flyway/drivers/mssql-jdbc-${MSSQL_JDBC_VERSION}.jar
RUN set -eux; \
    printf '%s  %s\n' "$MSSQL_JDBC_SHA256" "/flyway/drivers/mssql-jdbc-${MSSQL_JDBC_VERSION}.jar" | sha256sum -c -; \
    find /flyway -type f \( -name 'mssql-jdbc-*.jar' -o -name 'databricks-jdbc-*.jar' \) \
      ! -path "/flyway/drivers/mssql-jdbc-${MSSQL_JDBC_VERSION}.jar" -delete

WORKDIR /flyway

ENV FLYWAY_PLACEHOLDERS_API_IDENTITY_NAME=disabled-api \
    FLYWAY_PLACEHOLDERS_API_CLIENT_ID=00000000-0000-0000-0000-000000000000 \
    FLYWAY_PLACEHOLDERS_WORKER_IDENTITY_NAME=disabled-worker \
    FLYWAY_PLACEHOLDERS_WORKER_CLIENT_ID=00000000-0000-0000-0000-000000000000

COPY sql /flyway/sql

# Azure SQL Database does not allow switching databases with USE. The scripts
# target AssistantCoreDb for SQL Server compatibility, but Flyway already
# connects directly to the selected database, so remove those switches from
# the runtime migration bundle.
RUN find /flyway/sql -type f -name '*.sql' -exec sed -i '/^USE \[AssistantCoreDb\];$/d' {} +
