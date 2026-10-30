# syntax=docker/dockerfile:1
# check=skip=InvalidDefaultArgInFrom

# Modelled on https://github.com/hotio/whisparr (GPL-3.0), linux-amd64.Dockerfile
# on branch v3-develop; the root/ tree is copied from the same repo.
ARG BASE_DIGEST

FROM ghcr.io/hotio/base@${BASE_DIGEST}
EXPOSE 6969
ENV WEBUI_PORTS="6969/tcp"

RUN apk add --no-cache libintl sqlite-libs icu-libs

ARG BASE_DIGEST
ARG VERSION
ARG BRANCH
ARG REVISION
ARG PACKAGE_VERSION=${VERSION}

LABEL org.opencontainers.image.source="https://github.com/Whisparr/whisparr-next" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.base.digest="${BASE_DIGEST}" \
      org.opencontainers.image.base.name="ghcr.io/hotio/base:alpinevpn"

COPY app.tar.gz /tmp/app.tar.gz
RUN mkdir "${APP_DIR}/bin" && \
    tar xzf /tmp/app.tar.gz -C "${APP_DIR}/bin" --strip-components=1 && \
    rm -f /tmp/app.tar.gz && \
    rm -rf "${APP_DIR}/bin/Whisparr.Update" && \
    echo -e "PackageVersion=${PACKAGE_VERSION}\n\
PackageAuthor=[Whisparr](https://github.com/Whisparr)\n\
UpdateMethod=Docker\n\
Branch=${BRANCH}" > "${APP_DIR}/package_info" && \
    chmod -R u=rwX,go=rX "${APP_DIR}"

COPY root/ /
RUN find /etc/s6-overlay/s6-rc.d -name "run*" -execdir chmod +x {} +
