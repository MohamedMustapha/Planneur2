-- =============================================================================================================
-- Database roles (architecture.md §3).
--
--   app_owner  owns every schema and table; migrations run as this role.
--   app_rw     the runtime role. Deliberately NOT the owner, because Postgres exempts a table's owner from its
--              own RLS policies unless FORCE ROW LEVEL SECURITY is set — and relying on remembering FORCE on
--              every future table is exactly the kind of thing that eventually gets forgotten. A non-owner
--              runtime role means a missed FORCE cannot silently open the data up.
--
-- Run as a superuser, before AccessSchema.sql. Idempotent.
-- =============================================================================================================

do $$
begin
    if not exists (select 1 from pg_roles where rolname = 'app_owner') then
        execute format('create role app_owner login password %L', current_setting('cracra.app_owner_password', true));
    end if;

    if not exists (select 1 from pg_roles where rolname = 'app_rw') then
        execute format('create role app_rw login password %L', current_setting('cracra.app_rw_password', true));
    end if;
end
$$;

-- Keep the passwords in step with whatever the orchestrator generated this run.
do $$
begin
    if current_setting('cracra.app_owner_password', true) is not null then
        execute format('alter role app_owner password %L', current_setting('cracra.app_owner_password', true));
    end if;

    if current_setting('cracra.app_rw_password', true) is not null then
        execute format('alter role app_rw password %L', current_setting('cracra.app_rw_password', true));
    end if;
end
$$;

do $$
begin
    execute format('grant connect on database %I to app_owner, app_rw', current_database());

    -- app_owner creates the per-module schemas, so it needs CREATE on the database. app_rw deliberately does
    -- not: the runtime role can read and write rows and nothing else.
    execute format('grant create on database %I to app_owner', current_database());
end
$$;

-- app_rw may use what app_owner creates, but may never create or drop anything itself.
grant app_rw to app_owner;

revoke create on schema public from public;
