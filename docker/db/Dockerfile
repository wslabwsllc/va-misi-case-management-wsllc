# Database image for Compose `db`: the official PostgreSQL 18.6 Alpine image without gosu, run as the postgres user.
# The entrypoint uses gosu only to drop from root, so it is not needed when the container starts as postgres, and
# removing it removes the image's only HIGH/CRITICAL scan findings (docs/security-scan.md). The same upstream
# reference is PostgresDatabaseFixture.PostgresImage, so Testcontainers runs the same server build.
FROM postgres:18.6-alpine3.24@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873
RUN rm /usr/local/bin/gosu
USER postgres
