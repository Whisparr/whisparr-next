--
-- PostgreSQL database dump
--

\restrict RMnTeRcaAJdQcUMTaJHf1wusg4t3uab1orqiTkFf9Y1LTt6i3b4G85ScZX80l4x

-- Dumped from database version 16.15 (Debian 16.15-1.pgdg13+2)
-- Dumped by pg_dump version 16.15 (Debian 16.15-1.pgdg13+2)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: AutoTagging; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AutoTagging" (
    "Id" integer NOT NULL,
    "Name" text NOT NULL,
    "Specifications" text DEFAULT '[]'::text NOT NULL,
    "RemoveTagsAutomatically" boolean DEFAULT false NOT NULL,
    "Tags" text DEFAULT '[]'::text NOT NULL
);


--
-- Name: AutoTagging_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AutoTagging" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."AutoTagging_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Blocklist; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Blocklist" (
    "Id" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "EpisodeIds" text NOT NULL,
    "SourceTitle" text NOT NULL,
    "Quality" text NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "PublishedDate" timestamp with time zone,
    "Size" bigint,
    "Protocol" integer,
    "Indexer" text,
    "Message" text,
    "TorrentInfoHash" text,
    "Languages" text DEFAULT '[]'::text NOT NULL,
    "IndexerFlags" integer DEFAULT 0 NOT NULL,
    "Source" text
);


--
-- Name: Blocklist_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Blocklist" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Blocklist_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Commands; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Commands" (
    "Id" integer NOT NULL,
    "Name" text NOT NULL,
    "Body" text NOT NULL,
    "Priority" integer NOT NULL,
    "Status" integer NOT NULL,
    "QueuedAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone,
    "EndedAt" timestamp with time zone,
    "Duration" text,
    "Exception" text,
    "Trigger" integer NOT NULL,
    "Result" integer DEFAULT 1 NOT NULL
);


--
-- Name: Commands_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Commands" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Commands_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Config; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Config" (
    "Id" integer NOT NULL,
    "Key" text NOT NULL,
    "Value" text NOT NULL
);


--
-- Name: Config_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Config" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Config_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CustomFilters; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CustomFilters" (
    "Id" integer NOT NULL,
    "Type" text NOT NULL,
    "Label" text NOT NULL,
    "Filters" text NOT NULL
);


--
-- Name: CustomFilters_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CustomFilters" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."CustomFilters_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CustomFormats; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CustomFormats" (
    "Id" integer NOT NULL,
    "Name" text NOT NULL,
    "Specifications" text DEFAULT '[]'::text NOT NULL,
    "IncludeCustomFormatWhenRenaming" boolean DEFAULT false NOT NULL
);


--
-- Name: CustomFormats_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CustomFormats" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."CustomFormats_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DelayProfiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DelayProfiles" (
    "Id" integer NOT NULL,
    "EnableUsenet" boolean NOT NULL,
    "EnableTorrent" boolean NOT NULL,
    "PreferredProtocol" integer NOT NULL,
    "UsenetDelay" integer NOT NULL,
    "TorrentDelay" integer NOT NULL,
    "Order" integer NOT NULL,
    "Tags" text NOT NULL,
    "BypassIfHighestQuality" boolean DEFAULT false NOT NULL,
    "BypassIfAboveCustomFormatScore" boolean DEFAULT false NOT NULL,
    "MinimumCustomFormatScore" integer
);


--
-- Name: DelayProfiles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DelayProfiles" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."DelayProfiles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DownloadClientStatus; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DownloadClientStatus" (
    "Id" integer NOT NULL,
    "ProviderId" integer NOT NULL,
    "InitialFailure" timestamp with time zone,
    "MostRecentFailure" timestamp with time zone,
    "EscalationLevel" integer NOT NULL,
    "DisabledTill" timestamp with time zone
);


--
-- Name: DownloadClientStatus_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DownloadClientStatus" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."DownloadClientStatus_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DownloadClients; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DownloadClients" (
    "Id" integer NOT NULL,
    "Enable" boolean NOT NULL,
    "Name" text NOT NULL,
    "Implementation" text NOT NULL,
    "Settings" text NOT NULL,
    "ConfigContract" text NOT NULL,
    "Priority" integer DEFAULT 1 NOT NULL,
    "RemoveCompletedDownloads" boolean DEFAULT true NOT NULL,
    "RemoveFailedDownloads" boolean DEFAULT true NOT NULL,
    "Tags" text
);


--
-- Name: DownloadClients_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DownloadClients" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."DownloadClients_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DownloadHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DownloadHistory" (
    "Id" integer NOT NULL,
    "EventType" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "DownloadId" text NOT NULL,
    "SourceTitle" text NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "Protocol" integer,
    "IndexerId" integer,
    "DownloadClientId" integer,
    "Release" text,
    "Data" text
);


--
-- Name: DownloadHistory_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DownloadHistory" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."DownloadHistory_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: EpisodeFiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."EpisodeFiles" (
    "Id" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "Quality" text NOT NULL,
    "Size" bigint NOT NULL,
    "DateAdded" timestamp with time zone NOT NULL,
    "SeasonNumber" integer NOT NULL,
    "SceneName" text,
    "ReleaseGroup" text,
    "MediaInfo" text,
    "RelativePath" text,
    "Languages" text DEFAULT '[]'::text NOT NULL,
    "OriginalFilePath" text,
    "IndexerFlags" integer DEFAULT 0 NOT NULL
);


--
-- Name: EpisodeFiles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."EpisodeFiles" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."EpisodeFiles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Episodes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Episodes" (
    "Id" integer NOT NULL,
    "Monitored" boolean,
    "SeriesId" integer NOT NULL,
    "SeasonNumber" integer NOT NULL,
    "Title" text,
    "Overview" text,
    "Runtime" integer NOT NULL,
    "EpisodeFileId" integer,
    "AirDate" text,
    "AbsoluteEpisodeNumber" integer,
    "AirDateUtc" timestamp with time zone,
    "Ratings" text,
    "Images" text,
    "Actors" text,
    "LastSearchTime" timestamp with time zone,
    "TvdbId" integer,
    "ExternalId" text
);


--
-- Name: Episodes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Episodes" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Episodes_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ExtraFiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ExtraFiles" (
    "Id" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "SeasonNumber" integer NOT NULL,
    "EpisodeFileId" integer NOT NULL,
    "RelativePath" text NOT NULL,
    "Extension" text NOT NULL,
    "Added" timestamp with time zone NOT NULL,
    "LastUpdated" timestamp with time zone NOT NULL
);


--
-- Name: ExtraFiles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ExtraFiles" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ExtraFiles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: History; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."History" (
    "Id" integer NOT NULL,
    "EpisodeId" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "SourceTitle" text NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "Quality" text NOT NULL,
    "Data" text NOT NULL,
    "EventType" integer,
    "DownloadId" text,
    "Languages" text DEFAULT '[]'::text NOT NULL
);


--
-- Name: History_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."History" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."History_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ImportListExclusions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ImportListExclusions" (
    "Id" integer NOT NULL,
    "TvdbId" text NOT NULL,
    "Title" text NOT NULL
);


--
-- Name: ImportListExclusions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ImportListExclusions" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ImportListExclusions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ImportListStatus; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ImportListStatus" (
    "Id" integer NOT NULL,
    "ProviderId" integer NOT NULL,
    "InitialFailure" timestamp with time zone,
    "MostRecentFailure" timestamp with time zone,
    "EscalationLevel" integer NOT NULL,
    "DisabledTill" timestamp with time zone,
    "LastInfoSync" timestamp with time zone
);


--
-- Name: ImportListStatus_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ImportListStatus" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ImportListStatus_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ImportLists; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ImportLists" (
    "Id" integer NOT NULL,
    "Name" text NOT NULL,
    "Implementation" text NOT NULL,
    "Settings" text,
    "ConfigContract" text,
    "EnableAutomaticAdd" boolean,
    "RootFolderPath" text NOT NULL,
    "SiteMonitorType" integer NOT NULL,
    "QualityProfileId" integer NOT NULL,
    "Tags" text,
    "ShouldMonitor" integer DEFAULT 2 NOT NULL,
    "SearchForMissingEpisodes" boolean DEFAULT true NOT NULL,
    "MonitorNewItems" integer DEFAULT 0 NOT NULL
);


--
-- Name: ImportLists_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ImportLists" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ImportLists_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: IndexerStatus; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."IndexerStatus" (
    "Id" integer NOT NULL,
    "ProviderId" integer NOT NULL,
    "InitialFailure" timestamp with time zone,
    "MostRecentFailure" timestamp with time zone,
    "EscalationLevel" integer NOT NULL,
    "DisabledTill" timestamp with time zone,
    "LastRssSyncReleaseInfo" text
);


--
-- Name: IndexerStatus_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."IndexerStatus" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."IndexerStatus_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Indexers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Indexers" (
    "Id" integer NOT NULL,
    "EnableRss" boolean,
    "EnableAutomaticSearch" boolean NOT NULL,
    "EnableInteractiveSearch" boolean NOT NULL,
    "Name" text NOT NULL,
    "Implementation" text NOT NULL,
    "Settings" text,
    "ConfigContract" text,
    "Priority" integer DEFAULT 25 NOT NULL,
    "Tags" text,
    "DownloadClientId" integer DEFAULT 0 NOT NULL,
    "SeasonSearchMaximumSingleEpisodeAge" integer DEFAULT 0 NOT NULL
);


--
-- Name: Indexers_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Indexers" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Indexers_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Metadata; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Metadata" (
    "Id" integer NOT NULL,
    "Enable" boolean NOT NULL,
    "Name" text NOT NULL,
    "Implementation" text NOT NULL,
    "Settings" text NOT NULL,
    "ConfigContract" text NOT NULL
);


--
-- Name: MetadataFiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."MetadataFiles" (
    "Id" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "Consumer" text NOT NULL,
    "Type" integer NOT NULL,
    "RelativePath" text NOT NULL,
    "LastUpdated" timestamp with time zone NOT NULL,
    "SeasonNumber" integer,
    "EpisodeFileId" integer,
    "Hash" text,
    "Added" timestamp with time zone,
    "Extension" text NOT NULL
);


--
-- Name: MetadataFiles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."MetadataFiles" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."MetadataFiles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Metadata_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Metadata" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Metadata_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: NamingConfig; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."NamingConfig" (
    "Id" integer NOT NULL,
    "MultiEpisodeStyle" integer NOT NULL,
    "RenameEpisodes" boolean,
    "StandardEpisodeFormat" text,
    "SeriesFolderFormat" text,
    "ReplaceIllegalCharacters" boolean DEFAULT true NOT NULL,
    "ColonReplacementFormat" integer DEFAULT 4 NOT NULL,
    "JavEpisodeFormat" text DEFAULT '{External Id}'::text,
    "CustomColonReplacementFormat" text DEFAULT ''::text NOT NULL
);


--
-- Name: NamingConfig_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."NamingConfig" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."NamingConfig_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: NotificationStatus; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."NotificationStatus" (
    "Id" integer NOT NULL,
    "ProviderId" integer NOT NULL,
    "InitialFailure" timestamp with time zone,
    "MostRecentFailure" timestamp with time zone,
    "EscalationLevel" integer NOT NULL,
    "DisabledTill" timestamp with time zone
);


--
-- Name: NotificationStatus_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."NotificationStatus" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."NotificationStatus_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Notifications" (
    "Id" integer NOT NULL,
    "Name" text NOT NULL,
    "OnGrab" boolean NOT NULL,
    "OnDownload" boolean NOT NULL,
    "Settings" text NOT NULL,
    "Implementation" text NOT NULL,
    "ConfigContract" text,
    "OnUpgrade" boolean,
    "Tags" text,
    "OnRename" boolean NOT NULL,
    "OnHealthIssue" boolean NOT NULL,
    "IncludeHealthWarnings" boolean DEFAULT false NOT NULL,
    "OnSeriesDelete" boolean DEFAULT false NOT NULL,
    "OnEpisodeFileDelete" boolean DEFAULT false NOT NULL,
    "OnEpisodeFileDeleteForUpgrade" boolean DEFAULT true NOT NULL,
    "OnApplicationUpdate" boolean DEFAULT false NOT NULL,
    "OnSeriesAdd" boolean DEFAULT false NOT NULL,
    "OnHealthRestored" boolean DEFAULT false NOT NULL,
    "OnManualInteractionRequired" boolean DEFAULT false NOT NULL,
    "OnImportComplete" boolean DEFAULT false NOT NULL
);


--
-- Name: Notifications_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Notifications" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Notifications_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PendingReleases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PendingReleases" (
    "Id" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "Title" text NOT NULL,
    "Added" timestamp with time zone NOT NULL,
    "ParsedEpisodeInfo" text NOT NULL,
    "Release" text NOT NULL,
    "Reason" integer DEFAULT 0 NOT NULL,
    "AdditionalInfo" text
);


--
-- Name: PendingReleases_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PendingReleases" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."PendingReleases_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: QualityDefinitions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."QualityDefinitions" (
    "Id" integer NOT NULL,
    "Quality" integer NOT NULL,
    "Title" text NOT NULL
);


--
-- Name: QualityDefinitions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."QualityDefinitions" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."QualityDefinitions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: QualityProfiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."QualityProfiles" (
    "Id" integer NOT NULL,
    "Name" text NOT NULL,
    "Cutoff" integer NOT NULL,
    "Items" text NOT NULL,
    "UpgradeAllowed" boolean,
    "FormatItems" text DEFAULT '[]'::text NOT NULL,
    "MinFormatScore" integer DEFAULT 0 NOT NULL,
    "CutoffFormatScore" integer DEFAULT 0 NOT NULL,
    "MinUpgradeFormatScore" integer DEFAULT 1 NOT NULL
);


--
-- Name: QualityProfiles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."QualityProfiles" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."QualityProfiles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ReleaseProfiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ReleaseProfiles" (
    "Id" integer NOT NULL,
    "Required" text,
    "Ignored" text,
    "Tags" text NOT NULL,
    "Enabled" boolean DEFAULT true NOT NULL,
    "IndexerId" integer DEFAULT 0 NOT NULL,
    "Name" text,
    "ExcludedTags" text DEFAULT '[]'::text NOT NULL,
    "AirDateRestriction" boolean DEFAULT false NOT NULL,
    "AirDateGracePeriod" integer DEFAULT 0 NOT NULL
);


--
-- Name: ReleaseProfiles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ReleaseProfiles" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ReleaseProfiles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: RemotePathMappings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."RemotePathMappings" (
    "Id" integer NOT NULL,
    "Host" text NOT NULL,
    "RemotePath" text NOT NULL,
    "LocalPath" text NOT NULL
);


--
-- Name: RemotePathMappings_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."RemotePathMappings" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."RemotePathMappings_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: RootFolders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."RootFolders" (
    "Id" integer NOT NULL,
    "Path" text NOT NULL
);


--
-- Name: RootFolders_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."RootFolders" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."RootFolders_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ScheduledTasks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ScheduledTasks" (
    "Id" integer NOT NULL,
    "TypeName" text NOT NULL,
    "Interval" integer NOT NULL,
    "LastExecution" timestamp with time zone NOT NULL,
    "LastStartTime" timestamp with time zone
);


--
-- Name: ScheduledTasks_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ScheduledTasks" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ScheduledTasks_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Series; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Series" (
    "Id" integer NOT NULL,
    "TvdbId" integer NOT NULL,
    "Title" text NOT NULL,
    "TitleSlug" text,
    "CleanTitle" text NOT NULL,
    "Status" integer NOT NULL,
    "Overview" text,
    "Images" text NOT NULL,
    "Path" text NOT NULL,
    "Monitored" boolean NOT NULL,
    "QualityProfileId" integer NOT NULL,
    "LastInfoSync" timestamp with time zone,
    "LastDiskSync" timestamp with time zone,
    "Runtime" integer NOT NULL,
    "Network" text,
    "UseSceneNumbering" boolean NOT NULL,
    "FirstAired" timestamp with time zone,
    "NextAiring" timestamp with time zone,
    "Year" integer,
    "Seasons" text,
    "Ratings" text,
    "Genres" text,
    "Certification" text,
    "SortTitle" text,
    "Tags" text,
    "Added" timestamp with time zone,
    "AddOptions" text,
    "OriginalLanguage" integer DEFAULT 1 NOT NULL,
    "MonitorNewItems" integer DEFAULT 0 NOT NULL,
    "SeriesType" integer DEFAULT 0 NOT NULL,
    "LastAired" timestamp with time zone
);


--
-- Name: Series_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Series" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Series_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: SubtitleFiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."SubtitleFiles" (
    "Id" integer NOT NULL,
    "SeriesId" integer NOT NULL,
    "SeasonNumber" integer NOT NULL,
    "EpisodeFileId" integer NOT NULL,
    "RelativePath" text NOT NULL,
    "Extension" text NOT NULL,
    "Added" timestamp with time zone NOT NULL,
    "LastUpdated" timestamp with time zone NOT NULL,
    "Language" integer NOT NULL,
    "LanguageTags" text
);


--
-- Name: SubtitleFiles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."SubtitleFiles" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."SubtitleFiles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Tags; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Tags" (
    "Id" integer NOT NULL,
    "Label" text NOT NULL
);


--
-- Name: Tags_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Tags" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Tags_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Users" (
    "Id" integer NOT NULL,
    "Identifier" text NOT NULL,
    "Username" text NOT NULL,
    "Password" text NOT NULL,
    "Salt" text,
    "Iterations" integer
);


--
-- Name: Users_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Users" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Users_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: VersionInfo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."VersionInfo" (
    "Version" bigint NOT NULL,
    "AppliedOn" timestamp with time zone,
    "Description" character varying(1024)
);


--
-- Data for Name: AutoTagging; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Blocklist; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Commands; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Config; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: CustomFilters; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: CustomFormats; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: DelayProfiles; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public."DelayProfiles" OVERRIDING SYSTEM VALUE VALUES (1, true, true, 1, 0, 0, 2147483647, '[]', false, false, NULL);


--
-- Data for Name: DownloadClientStatus; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: DownloadClients; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: DownloadHistory; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: EpisodeFiles; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Episodes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: ExtraFiles; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: History; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: ImportListExclusions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: ImportListStatus; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: ImportLists; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: IndexerStatus; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Indexers; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Metadata; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public."Metadata" OVERRIDING SYSTEM VALUE VALUES (1, false, 'Kodi (XBMC) / Emby', 'XbmcMetadata', '{
  "seriesMetadata": true,
  "seriesMetadataUrl": false,
  "episodeMetadata": true,
  "episodeImageThumb": false,
  "seriesImages": true,
  "seasonImages": true,
  "episodeImages": true,
  "isValid": true
}', 'XbmcMetadataSettings');
INSERT INTO public."Metadata" OVERRIDING SYSTEM VALUE VALUES (2, false, 'WDTV', 'WdtvMetadata', '{
  "episodeMetadata": true,
  "seriesImages": true,
  "seasonImages": true,
  "episodeImages": true,
  "isValid": true
}', 'WdtvMetadataSettings');
INSERT INTO public."Metadata" OVERRIDING SYSTEM VALUE VALUES (3, false, 'Roksbox', 'RoksboxMetadata', '{
  "episodeMetadata": true,
  "seriesImages": true,
  "seasonImages": true,
  "episodeImages": true,
  "isValid": true
}', 'RoksboxMetadataSettings');
INSERT INTO public."Metadata" OVERRIDING SYSTEM VALUE VALUES (4, false, 'Plex', 'PlexMetadata', '{
  "seriesPlexMatchFile": true
}', 'PlexMetadataSettings');


--
-- Data for Name: MetadataFiles; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: NamingConfig; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: NotificationStatus; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Notifications; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: PendingReleases; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: QualityDefinitions; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (1, 0, 'Unknown');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (2, 1, 'SDTV');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (3, 12, 'WEBRip-480p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (4, 8, 'WEBDL-480p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (5, 2, 'DVD');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (6, 13, 'Bluray-480p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (7, 23, 'Bluray-576p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (8, 4, 'HDTV-720p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (9, 9, 'HDTV-1080p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (10, 10, 'Raw-HD');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (11, 14, 'WEBRip-720p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (12, 5, 'WEBDL-720p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (13, 6, 'Bluray-720p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (14, 15, 'WEBRip-1080p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (15, 3, 'WEBDL-1080p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (16, 7, 'Bluray-1080p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (17, 20, 'Bluray-1080p Remux');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (18, 16, 'HDTV-2160p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (19, 17, 'WEBRip-2160p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (20, 18, 'WEBDL-2160p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (21, 19, 'Bluray-2160p');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (22, 21, 'Bluray-2160p Remux');
INSERT INTO public."QualityDefinitions" OVERRIDING SYSTEM VALUE VALUES (23, 22, 'VR');


--
-- Data for Name: QualityProfiles; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public."QualityProfiles" OVERRIDING SYSTEM VALUE VALUES (1, 'Any', 1, '[
  {
    "quality": 0,
    "items": [],
    "allowed": false,
    "minSize": 1,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "quality": 1,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "id": 1000,
    "name": "WEB 480p",
    "items": [
      {
        "quality": 12,
        "items": [],
        "allowed": true,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      },
      {
        "quality": 8,
        "items": [],
        "allowed": true,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 2,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 13,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 23,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 4,
    "items": [],
    "allowed": true,
    "minSize": 3,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 9,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 10,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  },
  {
    "id": 1001,
    "name": "WEB 720p",
    "items": [
      {
        "quality": 14,
        "items": [],
        "allowed": true,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 5,
        "items": [],
        "allowed": true,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 6,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 130,
    "preferredSize": 95
  },
  {
    "id": 1002,
    "name": "WEB 1080p",
    "items": [
      {
        "quality": 15,
        "items": [],
        "allowed": true,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 3,
        "items": [],
        "allowed": true,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 7,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 155,
    "preferredSize": 95
  },
  {
    "quality": 20,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 16,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "id": 1003,
    "name": "WEB 2160p",
    "items": [
      {
        "quality": 17,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      },
      {
        "quality": 18,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 19,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 21,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 22,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  }
]', false, '[]', 0, 0, 1);
INSERT INTO public."QualityProfiles" OVERRIDING SYSTEM VALUE VALUES (2, 'SD', 1, '[
  {
    "quality": 0,
    "items": [],
    "allowed": false,
    "minSize": 1,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "quality": 1,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "id": 1000,
    "name": "WEB 480p",
    "items": [
      {
        "quality": 12,
        "items": [],
        "allowed": true,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      },
      {
        "quality": 8,
        "items": [],
        "allowed": true,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 2,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 13,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 23,
    "items": [],
    "allowed": true,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 4,
    "items": [],
    "allowed": false,
    "minSize": 3,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 9,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 10,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  },
  {
    "id": 1001,
    "name": "WEB 720p",
    "items": [
      {
        "quality": 14,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 5,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 6,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 130,
    "preferredSize": 95
  },
  {
    "id": 1002,
    "name": "WEB 1080p",
    "items": [
      {
        "quality": 15,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 3,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 7,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 155,
    "preferredSize": 95
  },
  {
    "quality": 20,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 16,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "id": 1003,
    "name": "WEB 2160p",
    "items": [
      {
        "quality": 17,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      },
      {
        "quality": 18,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 19,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 21,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 22,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  }
]', false, '[]', 0, 0, 1);
INSERT INTO public."QualityProfiles" OVERRIDING SYSTEM VALUE VALUES (3, 'HD-720p', 4, '[
  {
    "quality": 0,
    "items": [],
    "allowed": false,
    "minSize": 1,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "quality": 1,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "id": 1000,
    "name": "WEB 480p",
    "items": [
      {
        "quality": 12,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      },
      {
        "quality": 8,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 2,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 13,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 23,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 4,
    "items": [],
    "allowed": true,
    "minSize": 3,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 9,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 10,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  },
  {
    "id": 1001,
    "name": "WEB 720p",
    "items": [
      {
        "quality": 14,
        "items": [],
        "allowed": true,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 5,
        "items": [],
        "allowed": true,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 6,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 130,
    "preferredSize": 95
  },
  {
    "id": 1002,
    "name": "WEB 1080p",
    "items": [
      {
        "quality": 15,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 3,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 7,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 155,
    "preferredSize": 95
  },
  {
    "quality": 20,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 16,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "id": 1003,
    "name": "WEB 2160p",
    "items": [
      {
        "quality": 17,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      },
      {
        "quality": 18,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 19,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 21,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 22,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  }
]', false, '[]', 0, 0, 1);
INSERT INTO public."QualityProfiles" OVERRIDING SYSTEM VALUE VALUES (4, 'HD-1080p', 9, '[
  {
    "quality": 0,
    "items": [],
    "allowed": false,
    "minSize": 1,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "quality": 1,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "id": 1000,
    "name": "WEB 480p",
    "items": [
      {
        "quality": 12,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      },
      {
        "quality": 8,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 2,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 13,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 23,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 4,
    "items": [],
    "allowed": false,
    "minSize": 3,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 9,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 10,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  },
  {
    "id": 1001,
    "name": "WEB 720p",
    "items": [
      {
        "quality": 14,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 5,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 6,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 130,
    "preferredSize": 95
  },
  {
    "id": 1002,
    "name": "WEB 1080p",
    "items": [
      {
        "quality": 15,
        "items": [],
        "allowed": true,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 3,
        "items": [],
        "allowed": true,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 7,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 155,
    "preferredSize": 95
  },
  {
    "quality": 20,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 16,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "id": 1003,
    "name": "WEB 2160p",
    "items": [
      {
        "quality": 17,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      },
      {
        "quality": 18,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 19,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 21,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 22,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  }
]', false, '[]', 0, 0, 1);
INSERT INTO public."QualityProfiles" OVERRIDING SYSTEM VALUE VALUES (5, 'Ultra-HD', 16, '[
  {
    "quality": 0,
    "items": [],
    "allowed": false,
    "minSize": 1,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "quality": 1,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "id": 1000,
    "name": "WEB 480p",
    "items": [
      {
        "quality": 12,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      },
      {
        "quality": 8,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 2,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 13,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 23,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 4,
    "items": [],
    "allowed": false,
    "minSize": 3,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 9,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 10,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  },
  {
    "id": 1001,
    "name": "WEB 720p",
    "items": [
      {
        "quality": 14,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 5,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 6,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 130,
    "preferredSize": 95
  },
  {
    "id": 1002,
    "name": "WEB 1080p",
    "items": [
      {
        "quality": 15,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 3,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 7,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 155,
    "preferredSize": 95
  },
  {
    "quality": 20,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 16,
    "items": [],
    "allowed": true,
    "minSize": 35,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "id": 1003,
    "name": "WEB 2160p",
    "items": [
      {
        "quality": 17,
        "items": [],
        "allowed": true,
        "minSize": 35,
        "preferredSize": 95
      },
      {
        "quality": 18,
        "items": [],
        "allowed": true,
        "minSize": 35,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 19,
    "items": [],
    "allowed": true,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 21,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 22,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  }
]', false, '[]', 0, 0, 1);
INSERT INTO public."QualityProfiles" OVERRIDING SYSTEM VALUE VALUES (6, 'HD - 720p/1080p', 4, '[
  {
    "quality": 0,
    "items": [],
    "allowed": false,
    "minSize": 1,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "quality": 1,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "id": 1000,
    "name": "WEB 480p",
    "items": [
      {
        "quality": 12,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      },
      {
        "quality": 8,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 2,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 13,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 23,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 4,
    "items": [],
    "allowed": true,
    "minSize": 3,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 9,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 10,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  },
  {
    "id": 1001,
    "name": "WEB 720p",
    "items": [
      {
        "quality": 14,
        "items": [],
        "allowed": true,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 5,
        "items": [],
        "allowed": true,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 6,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 130,
    "preferredSize": 95
  },
  {
    "id": 1002,
    "name": "WEB 1080p",
    "items": [
      {
        "quality": 15,
        "items": [],
        "allowed": true,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 3,
        "items": [],
        "allowed": true,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": true
  },
  {
    "quality": 7,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "maxSize": 155,
    "preferredSize": 95
  },
  {
    "quality": 20,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 16,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "id": 1003,
    "name": "WEB 2160p",
    "items": [
      {
        "quality": 17,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      },
      {
        "quality": 18,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 19,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 21,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 22,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  }
]', false, '[]', 0, 0, 1);
INSERT INTO public."QualityProfiles" OVERRIDING SYSTEM VALUE VALUES (7, 'VR', 22, '[
  {
    "quality": 0,
    "items": [],
    "allowed": false,
    "minSize": 1,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "quality": 1,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "id": 1000,
    "name": "WEB 480p",
    "items": [
      {
        "quality": 12,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      },
      {
        "quality": 8,
        "items": [],
        "allowed": false,
        "minSize": 2,
        "maxSize": 100,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 2,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 13,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 23,
    "items": [],
    "allowed": false,
    "minSize": 2,
    "maxSize": 100,
    "preferredSize": 95
  },
  {
    "quality": 4,
    "items": [],
    "allowed": false,
    "minSize": 3,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 9,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 125,
    "preferredSize": 95
  },
  {
    "quality": 10,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "preferredSize": 95
  },
  {
    "id": 1001,
    "name": "WEB 720p",
    "items": [
      {
        "quality": 14,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 5,
        "items": [],
        "allowed": false,
        "minSize": 3,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 6,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 130,
    "preferredSize": 95
  },
  {
    "id": 1002,
    "name": "WEB 1080p",
    "items": [
      {
        "quality": 15,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      },
      {
        "quality": 3,
        "items": [],
        "allowed": false,
        "minSize": 4,
        "maxSize": 130,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 7,
    "items": [],
    "allowed": false,
    "minSize": 4,
    "maxSize": 155,
    "preferredSize": 95
  },
  {
    "quality": 20,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 16,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "maxSize": 199.9,
    "preferredSize": 95
  },
  {
    "id": 1003,
    "name": "WEB 2160p",
    "items": [
      {
        "quality": 17,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      },
      {
        "quality": 18,
        "items": [],
        "allowed": false,
        "minSize": 35,
        "preferredSize": 95
      }
    ],
    "allowed": false
  },
  {
    "quality": 19,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 21,
    "items": [],
    "allowed": false,
    "minSize": 35,
    "preferredSize": 95
  },
  {
    "quality": 22,
    "items": [],
    "allowed": true,
    "minSize": 4,
    "preferredSize": 95
  }
]', false, '[]', 0, 0, 1);


--
-- Data for Name: ReleaseProfiles; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: RemotePathMappings; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: RootFolders; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: ScheduledTasks; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (1, 'NzbDrone.Core.Download.RefreshMonitoredDownloadsCommand', 1, '2026-10-29 11:38:52.216752+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (2, 'NzbDrone.Core.Messaging.Commands.MessagingCleanupCommand', 5, '2026-10-29 11:38:52.218858+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (3, 'NzbDrone.Core.Update.Commands.ApplicationUpdateCheckCommand', 360, '2026-10-29 11:38:52.219617+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (4, 'NzbDrone.Core.HealthCheck.CheckHealthCommand', 360, '2026-10-29 11:38:52.220236+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (5, 'NzbDrone.Core.Tv.Commands.RefreshSeriesCommand', 720, '2026-10-29 11:38:52.220811+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (6, 'NzbDrone.Core.Housekeeping.HousekeepingCommand', 1440, '2026-10-29 11:38:52.221368+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (7, 'NzbDrone.Core.MediaFiles.Commands.CleanUpRecycleBinCommand', 1440, '2026-10-29 11:38:52.22192+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (8, 'NzbDrone.Core.ImportLists.ImportListSyncCommand', 5, '2026-10-29 11:38:52.222467+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (9, 'NzbDrone.Core.Backup.BackupCommand', 10080, '2026-10-29 11:38:52.223042+00', '0001-01-01 04:57:00+00');
INSERT INTO public."ScheduledTasks" OVERRIDING SYSTEM VALUE VALUES (10, 'NzbDrone.Core.Indexers.RssSyncCommand', 15, '2026-10-29 11:38:52.22349+00', '0001-01-01 04:57:00+00');


--
-- Data for Name: Series; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: SubtitleFiles; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Tags; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: Users; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: VersionInfo; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public."VersionInfo" VALUES (1, '2026-10-29 11:38:51.770961+00', 'InitialSetup');
INSERT INTO public."VersionInfo" VALUES (2, '2026-10-29 11:38:51.887382+00', 'remove_season_folders');
INSERT INTO public."VersionInfo" VALUES (3, '2026-10-29 11:38:51.891115+00', 'remove_rarbg');
INSERT INTO public."VersionInfo" VALUES (4, '2026-10-29 11:38:51.892997+00', 'add_result_to_commands');
INSERT INTO public."VersionInfo" VALUES (5, '2026-10-29 11:38:51.894691+00', 'add_on_series_add_to_notifications');
INSERT INTO public."VersionInfo" VALUES (6, '2026-10-29 11:38:51.896278+00', 'add_colon_replacement_to_naming_config');
INSERT INTO public."VersionInfo" VALUES (7, '2026-10-29 11:38:51.901591+00', 'health_restored_notification');
INSERT INTO public."VersionInfo" VALUES (8, '2026-10-29 11:38:51.903536+00', 'clean_episode_model');
INSERT INTO public."VersionInfo" VALUES (9, '2026-10-29 11:38:51.908191+00', 'import_list_monitor');
INSERT INTO public."VersionInfo" VALUES (10, '2026-10-29 11:38:51.910087+00', 'add_download_client_tags');
INSERT INTO public."VersionInfo" VALUES (11, '2026-10-29 11:38:51.911503+00', 'add_notification_status');
INSERT INTO public."VersionInfo" VALUES (12, '2026-10-29 11:38:51.917494+00', 'parse_language_tags_from_existing_subtitle_files');
INSERT INTO public."VersionInfo" VALUES (13, '2026-10-29 11:38:51.920362+00', 'list_add_missing_search');
INSERT INTO public."VersionInfo" VALUES (14, '2026-10-29 11:38:51.921824+00', 'AddNewItemMonitorType');
INSERT INTO public."VersionInfo" VALUES (15, '2026-10-29 11:38:51.923701+00', 'AddEpisodeExternalId');
INSERT INTO public."VersionInfo" VALUES (16, '2026-10-29 11:38:51.92506+00', 'AddSeriesTypeAndJavFormat');
INSERT INTO public."VersionInfo" VALUES (17, '2026-10-29 11:38:51.926861+00', 'import_exclusion_type');
INSERT INTO public."VersionInfo" VALUES (18, '2026-10-29 11:38:51.927887+00', 'series_last_aired');
INSERT INTO public."VersionInfo" VALUES (202, '2026-10-29 11:38:51.929185+00', 'add_indexer_flags');
INSERT INTO public."VersionInfo" VALUES (209, '2026-10-29 11:38:51.931083+00', 'add_on_import_complete_to_notifications');
INSERT INTO public."VersionInfo" VALUES (210, '2026-10-29 11:38:51.932832+00', 'add_monitored_seasons_filter');
INSERT INTO public."VersionInfo" VALUES (211, '2026-10-29 11:38:51.934206+00', 'add_custom_colon_replacement_to_naming_config');
INSERT INTO public."VersionInfo" VALUES (212, '2026-10-29 11:38:51.935434+00', 'add_minium_upgrade_format_score_to_quality_profiles');
INSERT INTO public."VersionInfo" VALUES (214, '2026-10-29 11:38:51.93651+00', 'add_blurary576p_quality_in_profiles');
INSERT INTO public."VersionInfo" VALUES (215, '2026-10-29 11:38:51.937526+00', 'add_blurary576p_quality_in_profiles_with_grouped_blurary480p');
INSERT INTO public."VersionInfo" VALUES (216, '2026-10-29 11:38:51.939315+00', 'telegram_link_preview');
INSERT INTO public."VersionInfo" VALUES (217, '2026-10-29 11:38:51.940697+00', 'add_size_to_quality_profiles');
INSERT INTO public."VersionInfo" VALUES (218, '2026-10-29 11:38:51.943504+00', 'add_exclusion_tags_to_release_profiles');
INSERT INTO public."VersionInfo" VALUES (219, '2026-10-29 11:38:51.944752+00', 'add_source_to_blocklist');
INSERT INTO public."VersionInfo" VALUES (220, '2026-10-29 11:38:51.946027+00', 'add_air_date_filtering_to_release_profiles');
INSERT INTO public."VersionInfo" VALUES (221, '2026-10-29 11:38:51.94763+00', 'add_episode_external_id_index');


--
-- Name: AutoTagging_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."AutoTagging_Id_seq"', 1, false);


--
-- Name: Blocklist_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Blocklist_Id_seq"', 1, false);


--
-- Name: Commands_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Commands_Id_seq"', 1, false);


--
-- Name: Config_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Config_Id_seq"', 1, false);


--
-- Name: CustomFilters_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CustomFilters_Id_seq"', 1, false);


--
-- Name: CustomFormats_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CustomFormats_Id_seq"', 1, false);


--
-- Name: DelayProfiles_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DelayProfiles_Id_seq"', 1, true);


--
-- Name: DownloadClientStatus_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DownloadClientStatus_Id_seq"', 1, false);


--
-- Name: DownloadClients_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DownloadClients_Id_seq"', 1, false);


--
-- Name: DownloadHistory_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DownloadHistory_Id_seq"', 1, false);


--
-- Name: EpisodeFiles_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."EpisodeFiles_Id_seq"', 1, false);


--
-- Name: Episodes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Episodes_Id_seq"', 1, false);


--
-- Name: ExtraFiles_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ExtraFiles_Id_seq"', 1, false);


--
-- Name: History_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."History_Id_seq"', 1, false);


--
-- Name: ImportListExclusions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ImportListExclusions_Id_seq"', 1, false);


--
-- Name: ImportListStatus_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ImportListStatus_Id_seq"', 1, false);


--
-- Name: ImportLists_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ImportLists_Id_seq"', 1, false);


--
-- Name: IndexerStatus_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."IndexerStatus_Id_seq"', 1, false);


--
-- Name: Indexers_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Indexers_Id_seq"', 1, false);


--
-- Name: MetadataFiles_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."MetadataFiles_Id_seq"', 1, false);


--
-- Name: Metadata_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Metadata_Id_seq"', 4, true);


--
-- Name: NamingConfig_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."NamingConfig_Id_seq"', 1, false);


--
-- Name: NotificationStatus_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."NotificationStatus_Id_seq"', 1, false);


--
-- Name: Notifications_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Notifications_Id_seq"', 1, false);


--
-- Name: PendingReleases_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PendingReleases_Id_seq"', 1, false);


--
-- Name: QualityDefinitions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."QualityDefinitions_Id_seq"', 23, true);


--
-- Name: QualityProfiles_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."QualityProfiles_Id_seq"', 7, true);


--
-- Name: ReleaseProfiles_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ReleaseProfiles_Id_seq"', 1, false);


--
-- Name: RemotePathMappings_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RemotePathMappings_Id_seq"', 1, false);


--
-- Name: RootFolders_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RootFolders_Id_seq"', 1, false);


--
-- Name: ScheduledTasks_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ScheduledTasks_Id_seq"', 10, true);


--
-- Name: Series_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Series_Id_seq"', 1, false);


--
-- Name: SubtitleFiles_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."SubtitleFiles_Id_seq"', 1, false);


--
-- Name: Tags_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Tags_Id_seq"', 1, false);


--
-- Name: Users_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Users_Id_seq"', 1, false);


--
-- Name: AutoTagging PK_AutoTagging; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AutoTagging"
    ADD CONSTRAINT "PK_AutoTagging" PRIMARY KEY ("Id");


--
-- Name: Blocklist PK_Blocklist; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Blocklist"
    ADD CONSTRAINT "PK_Blocklist" PRIMARY KEY ("Id");


--
-- Name: Commands PK_Commands; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Commands"
    ADD CONSTRAINT "PK_Commands" PRIMARY KEY ("Id");


--
-- Name: Config PK_Config; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Config"
    ADD CONSTRAINT "PK_Config" PRIMARY KEY ("Id");


--
-- Name: CustomFilters PK_CustomFilters; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomFilters"
    ADD CONSTRAINT "PK_CustomFilters" PRIMARY KEY ("Id");


--
-- Name: CustomFormats PK_CustomFormats; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomFormats"
    ADD CONSTRAINT "PK_CustomFormats" PRIMARY KEY ("Id");


--
-- Name: DelayProfiles PK_DelayProfiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DelayProfiles"
    ADD CONSTRAINT "PK_DelayProfiles" PRIMARY KEY ("Id");


--
-- Name: DownloadClientStatus PK_DownloadClientStatus; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DownloadClientStatus"
    ADD CONSTRAINT "PK_DownloadClientStatus" PRIMARY KEY ("Id");


--
-- Name: DownloadClients PK_DownloadClients; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DownloadClients"
    ADD CONSTRAINT "PK_DownloadClients" PRIMARY KEY ("Id");


--
-- Name: DownloadHistory PK_DownloadHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DownloadHistory"
    ADD CONSTRAINT "PK_DownloadHistory" PRIMARY KEY ("Id");


--
-- Name: EpisodeFiles PK_EpisodeFiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EpisodeFiles"
    ADD CONSTRAINT "PK_EpisodeFiles" PRIMARY KEY ("Id");


--
-- Name: Episodes PK_Episodes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Episodes"
    ADD CONSTRAINT "PK_Episodes" PRIMARY KEY ("Id");


--
-- Name: ExtraFiles PK_ExtraFiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ExtraFiles"
    ADD CONSTRAINT "PK_ExtraFiles" PRIMARY KEY ("Id");


--
-- Name: History PK_History; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."History"
    ADD CONSTRAINT "PK_History" PRIMARY KEY ("Id");


--
-- Name: ImportListExclusions PK_ImportListExclusions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ImportListExclusions"
    ADD CONSTRAINT "PK_ImportListExclusions" PRIMARY KEY ("Id");


--
-- Name: ImportListStatus PK_ImportListStatus; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ImportListStatus"
    ADD CONSTRAINT "PK_ImportListStatus" PRIMARY KEY ("Id");


--
-- Name: ImportLists PK_ImportLists; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ImportLists"
    ADD CONSTRAINT "PK_ImportLists" PRIMARY KEY ("Id");


--
-- Name: IndexerStatus PK_IndexerStatus; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."IndexerStatus"
    ADD CONSTRAINT "PK_IndexerStatus" PRIMARY KEY ("Id");


--
-- Name: Indexers PK_Indexers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Indexers"
    ADD CONSTRAINT "PK_Indexers" PRIMARY KEY ("Id");


--
-- Name: Metadata PK_Metadata; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Metadata"
    ADD CONSTRAINT "PK_Metadata" PRIMARY KEY ("Id");


--
-- Name: MetadataFiles PK_MetadataFiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."MetadataFiles"
    ADD CONSTRAINT "PK_MetadataFiles" PRIMARY KEY ("Id");


--
-- Name: NamingConfig PK_NamingConfig; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."NamingConfig"
    ADD CONSTRAINT "PK_NamingConfig" PRIMARY KEY ("Id");


--
-- Name: NotificationStatus PK_NotificationStatus; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."NotificationStatus"
    ADD CONSTRAINT "PK_NotificationStatus" PRIMARY KEY ("Id");


--
-- Name: Notifications PK_Notifications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Notifications"
    ADD CONSTRAINT "PK_Notifications" PRIMARY KEY ("Id");


--
-- Name: PendingReleases PK_PendingReleases; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PendingReleases"
    ADD CONSTRAINT "PK_PendingReleases" PRIMARY KEY ("Id");


--
-- Name: QualityDefinitions PK_QualityDefinitions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QualityDefinitions"
    ADD CONSTRAINT "PK_QualityDefinitions" PRIMARY KEY ("Id");


--
-- Name: QualityProfiles PK_QualityProfiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QualityProfiles"
    ADD CONSTRAINT "PK_QualityProfiles" PRIMARY KEY ("Id");


--
-- Name: ReleaseProfiles PK_ReleaseProfiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ReleaseProfiles"
    ADD CONSTRAINT "PK_ReleaseProfiles" PRIMARY KEY ("Id");


--
-- Name: RemotePathMappings PK_RemotePathMappings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RemotePathMappings"
    ADD CONSTRAINT "PK_RemotePathMappings" PRIMARY KEY ("Id");


--
-- Name: RootFolders PK_RootFolders; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RootFolders"
    ADD CONSTRAINT "PK_RootFolders" PRIMARY KEY ("Id");


--
-- Name: ScheduledTasks PK_ScheduledTasks; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ScheduledTasks"
    ADD CONSTRAINT "PK_ScheduledTasks" PRIMARY KEY ("Id");


--
-- Name: Series PK_Series; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Series"
    ADD CONSTRAINT "PK_Series" PRIMARY KEY ("Id");


--
-- Name: SubtitleFiles PK_SubtitleFiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."SubtitleFiles"
    ADD CONSTRAINT "PK_SubtitleFiles" PRIMARY KEY ("Id");


--
-- Name: Tags PK_Tags; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Tags"
    ADD CONSTRAINT "PK_Tags" PRIMARY KEY ("Id");


--
-- Name: Users PK_Users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");


--
-- Name: IX_AutoTagging_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_AutoTagging_Name" ON public."AutoTagging" USING btree ("Name");


--
-- Name: IX_Blocklist_SeriesId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Blocklist_SeriesId" ON public."Blocklist" USING btree ("SeriesId");


--
-- Name: IX_Config_Key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Config_Key" ON public."Config" USING btree ("Key");


--
-- Name: IX_CustomFormats_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_CustomFormats_Name" ON public."CustomFormats" USING btree ("Name");


--
-- Name: IX_DownloadClientStatus_ProviderId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_DownloadClientStatus_ProviderId" ON public."DownloadClientStatus" USING btree ("ProviderId");


--
-- Name: IX_DownloadHistory_DownloadId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DownloadHistory_DownloadId" ON public."DownloadHistory" USING btree ("DownloadId");


--
-- Name: IX_DownloadHistory_EventType; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DownloadHistory_EventType" ON public."DownloadHistory" USING btree ("EventType");


--
-- Name: IX_DownloadHistory_SeriesId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DownloadHistory_SeriesId" ON public."DownloadHistory" USING btree ("SeriesId");


--
-- Name: IX_EpisodeFiles_SeriesId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_EpisodeFiles_SeriesId" ON public."EpisodeFiles" USING btree ("SeriesId");


--
-- Name: IX_Episodes_EpisodeFileId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Episodes_EpisodeFileId" ON public."Episodes" USING btree ("EpisodeFileId");


--
-- Name: IX_Episodes_ExternalId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Episodes_ExternalId" ON public."Episodes" USING btree ("ExternalId");


--
-- Name: IX_Episodes_SeriesId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Episodes_SeriesId" ON public."Episodes" USING btree ("SeriesId");


--
-- Name: IX_Episodes_SeriesId_AirDate; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Episodes_SeriesId_AirDate" ON public."Episodes" USING btree ("SeriesId", "AirDate");


--
-- Name: IX_History_Date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_History_Date" ON public."History" USING btree ("Date");


--
-- Name: IX_History_DownloadId_Date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_History_DownloadId_Date" ON public."History" USING btree ("DownloadId", "Date" DESC);


--
-- Name: IX_History_EpisodeId_Date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_History_EpisodeId_Date" ON public."History" USING btree ("EpisodeId", "Date" DESC);


--
-- Name: IX_History_EventType; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_History_EventType" ON public."History" USING btree ("EventType");


--
-- Name: IX_History_SeriesId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_History_SeriesId" ON public."History" USING btree ("SeriesId");


--
-- Name: IX_ImportListExclusions_TvdbId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ImportListExclusions_TvdbId" ON public."ImportListExclusions" USING btree ("TvdbId");


--
-- Name: IX_ImportListStatus_ProviderId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ImportListStatus_ProviderId" ON public."ImportListStatus" USING btree ("ProviderId");


--
-- Name: IX_ImportLists_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ImportLists_Name" ON public."ImportLists" USING btree ("Name");


--
-- Name: IX_IndexerStatus_ProviderId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_IndexerStatus_ProviderId" ON public."IndexerStatus" USING btree ("ProviderId");


--
-- Name: IX_Indexers_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Indexers_Name" ON public."Indexers" USING btree ("Name");


--
-- Name: IX_NotificationStatus_ProviderId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_NotificationStatus_ProviderId" ON public."NotificationStatus" USING btree ("ProviderId");


--
-- Name: IX_QualityDefinitions_Quality; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_QualityDefinitions_Quality" ON public."QualityDefinitions" USING btree ("Quality");


--
-- Name: IX_QualityDefinitions_Title; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_QualityDefinitions_Title" ON public."QualityDefinitions" USING btree ("Title");


--
-- Name: IX_QualityProfiles_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_QualityProfiles_Name" ON public."QualityProfiles" USING btree ("Name");


--
-- Name: IX_RootFolders_Path; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_RootFolders_Path" ON public."RootFolders" USING btree ("Path");


--
-- Name: IX_ScheduledTasks_TypeName; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ScheduledTasks_TypeName" ON public."ScheduledTasks" USING btree ("TypeName");


--
-- Name: IX_Series_CleanTitle; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Series_CleanTitle" ON public."Series" USING btree ("CleanTitle");


--
-- Name: IX_Series_Path; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Series_Path" ON public."Series" USING btree ("Path");


--
-- Name: IX_Series_TvdbId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Series_TvdbId" ON public."Series" USING btree ("TvdbId");


--
-- Name: IX_Tags_Label; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Tags_Label" ON public."Tags" USING btree ("Label");


--
-- Name: IX_Users_Identifier; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Users_Identifier" ON public."Users" USING btree ("Identifier");


--
-- Name: IX_Users_Username; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Users_Username" ON public."Users" USING btree ("Username");


--
-- Name: UC_Version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UC_Version" ON public."VersionInfo" USING btree ("Version");


--
-- PostgreSQL database dump complete
--

\unrestrict RMnTeRcaAJdQcUMTaJHf1wusg4t3uab1orqiTkFf9Y1LTt6i3b4G85ScZX80l4x

