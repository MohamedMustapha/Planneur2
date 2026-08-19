-- =============================================================================================================
-- access schema — the executable form of visibility-matrix.md §2 and §3.
--
-- Everything here is idempotent: the platform migrator runs this file on every start so the predicate functions
-- can evolve without a hand-written migration per change. Policies that *use* these functions live in each
-- module's own EF migration (conventions.md §4), which is what keeps the matrix in one place while letting each
-- slice own its tables.
--
-- S0 ships the session accessors only. The per-entity predicates (can_read_activity, can_read_project, ...) need
-- business tables to reference and therefore land with S2.
-- =============================================================================================================

create schema if not exists access;

-- -------------------------------------------------------------------------------------------------------------
-- Session accessors. Each reads one of the GUCs the RlsSessionInterceptor stamps on connection open.
-- `true` in current_setting means "return NULL if unset" rather than raising — an unstamped connection must
-- evaluate to no access, never to an error that a caller could mistake for a transient fault.
-- -------------------------------------------------------------------------------------------------------------

create or replace function access.uid() returns uuid
    language sql stable
    as $$ select nullif(current_setting('app.user_id', true), '')::uuid $$;

create or replace function access.unit() returns uuid
    language sql stable
    as $$ select nullif(current_setting('app.unit_id', true), '')::uuid $$;

create or replace function access.depts() returns uuid[]
    language sql stable
    as $$
        select coalesce(
            string_to_array(nullif(current_setting('app.dept_ids', true), ''), ',')::uuid[],
            '{}'::uuid[]
        )
    $$;

create or replace function access.roles() returns text[]
    language sql stable
    as $$
        select coalesce(
            string_to_array(nullif(current_setting('app.roles', true), ''), ','),
            '{}'::text[]
        )
    $$;

-- -------------------------------------------------------------------------------------------------------------
-- Role predicates.
-- -------------------------------------------------------------------------------------------------------------

create or replace function access.has(p_role text) returns boolean
    language sql stable
    as $$ select p_role = any(access.roles()) $$;

create or replace function access.is_head() returns boolean
    language sql stable
    as $$ select access.has('unit-head') or access.has('dept-head') or access.has('pmo') $$;

-- Background jobs (sync, AI, outbox drain) run under this role. Policies opt in to it explicitly; it is never a
-- blanket bypass, and the interceptor refuses to put it in a token-derived context.
create or replace function access.is_system() returns boolean
    language sql stable
    as $$ select access.has('system') $$;

-- True when the session has no scope at all. Useful in tests and in the /api/ping proof, and a readable way for a
-- policy to say "an unstamped connection sees nothing".
create or replace function access.is_scoped() returns boolean
    language sql stable
    as $$ select access.uid() is not null $$;

-- -------------------------------------------------------------------------------------------------------------
-- Grants. app_rw executes the predicates but owns nothing, so FORCE ROW LEVEL SECURITY applies to it.
-- -------------------------------------------------------------------------------------------------------------

grant usage on schema access to app_rw, app_owner;
grant execute on all functions in schema access to app_rw, app_owner;

alter default privileges in schema access grant execute on functions to app_rw, app_owner;
