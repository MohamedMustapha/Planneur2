#!/usr/bin/env python3
"""Generates deploy/keycloak/cracra-realm.json.

The realm is generated rather than hand-edited because the seed org has to agree, id for id, with three other
places: the RLS matrix test in tests/Integration, the Playwright login helper, and visibility-matrix.md. Writing
those ids once and emitting the JSON is the only way that stays true.

Run:  python deploy/keycloak/build-realm.py
"""

import json
import pathlib

REALM = "cracra"

# --- The seed organisation -------------------------------------------------------------------------------------
# Three departments. DSI is the IT directorate the product is built around: a director and a deputy director above
# five unit heads, one per unit. Communication sits beside it as a peer directorate — same level, different trade —
# which is what gives the cross-department rules something real to be visible on. DAF stays as it was, because the
# RLS matrix test and the Playwright suite both assert against Sofia and Laurent by id.

DEPARTMENTS = {
    "dsi": ("11111111-1111-1111-1111-111111111111", "Direction des Systèmes d'Information"),
    "daf": ("22222222-2222-2222-2222-222222222222", "Direction Financière"),
    "comm": ("33333333-3333-3333-3333-333333333333", "Direction de la Communication"),
}

# (id, display name, owning department, unit kind)
#
# The two DSI unit ids are the originals: `infra` became `ops` and `etudes` became `dev`, renamed rather than
# replaced so every id SeedOrganisation and the E2E suite hold onto still resolves to the same unit.
UNITS = {
    "ops": ("aaaaaaaa-0000-0000-0000-000000000001", "Exploitation & Production", "dsi", "Run"),
    "dev": ("aaaaaaaa-0000-0000-0000-000000000002", "Études & Développement", "dsi", "Delivery"),
    "secu": ("aaaaaaaa-0000-0000-0000-000000000003", "Sécurité & Conformité", "dsi", "Support"),
    "helpdesk": ("aaaaaaaa-0000-0000-0000-000000000004", "Support & Assistance", "dsi", "Support"),
    "transfo": ("aaaaaaaa-0000-0000-0000-000000000005", "Transformation Digitale", "dsi", "Admin"),
    "compta": ("bbbbbbbb-0000-0000-0000-000000000001", "Comptabilité", "daf", "Admin"),
    "controle": ("bbbbbbbb-0000-0000-0000-000000000002", "Contrôle de gestion", "daf", "Admin"),
    "design": ("cccccccc-0000-0000-0000-000000000001", "Studio Design & Contenus", "comm", "Delivery"),
}

# (id, username, first, last, unit, functional role, contextual roles)
#
# The hierarchy is expressed in two columns at once, and deliberately so. The functional role is the job title the
# org chart draws — `directeur` above `directeur-adjoint` above `chef-de-pole` — while the contextual role is what
# the person may see. A directorate's deputy holds dept-head because that is genuinely their scope; a unit head
# holds unit-head and no more. Conflating the two would make every promotion an access-control change.
PEOPLE = [
    # --- DSI: the directorate ------------------------------------------------------------------------------------
    ("c0000000-0000-0000-0000-000000000006", "olivier.marchand", "Olivier", "Marchand", "dev", "directeur", ["member", "dept-head"]),
    ("c0000000-0000-0000-0000-00000000000b", "helene.vasseur", "Hélène", "Vasseur", "transfo", "directeur-adjoint", ["member", "dept-head"]),

    # --- DSI / Exploitation & Production (Ops) -------------------------------------------------------------------
    ("c0000000-0000-0000-0000-000000000004", "thomas.berthier", "Thomas", "Berthier", "ops", "chef-de-pole", ["member", "unit-head"]),
    ("c0000000-0000-0000-0000-000000000001", "camille.villeneuve", "Camille", "Villeneuve", "ops", "architecte", ["member"]),
    ("c0000000-0000-0000-0000-000000000002", "mehdi.sadaoui", "Mehdi", "Sadaoui", "ops", "ops", ["member"]),
    ("c0000000-0000-0000-0000-000000000003", "anais.lefevre", "Anaïs", "Lefèvre", "ops", "ops", ["member"]),
    ("c0000000-0000-0000-0000-000000000014", "tarek.amrani", "Tarek", "Amrani", "ops", "ops", ["member"]),

    # --- DSI / Études & Développement (Dev) ----------------------------------------------------------------------
    ("c0000000-0000-0000-0000-00000000000c", "karim.benali", "Karim", "Benali", "dev", "chef-de-pole", ["member", "unit-head"]),
    ("c0000000-0000-0000-0000-00000000000a", "pierre.dubois", "Pierre", "Dubois", "dev", "tech-lead", ["member", "project-lead"]),
    ("c0000000-0000-0000-0000-000000000005", "julie.ondracek", "Julie", "Ondracek", "dev", "dev", ["member"]),
    ("c0000000-0000-0000-0000-00000000000d", "lea.fontaine", "Léa", "Fontaine", "dev", "dev", ["member"]),

    # --- DSI / Sécurité & Conformité (Secu) ----------------------------------------------------------------------
    ("c0000000-0000-0000-0000-00000000000e", "marc.delaunay", "Marc", "Delaunay", "secu", "chef-de-pole", ["member", "unit-head"]),
    ("c0000000-0000-0000-0000-00000000000f", "ines.bourgeois", "Inès", "Bourgeois", "secu", "rssi", ["member"]),

    # --- DSI / Support & Assistance (Helpdesk) -------------------------------------------------------------------
    ("c0000000-0000-0000-0000-000000000010", "yann.corbier", "Yann", "Corbier", "helpdesk", "chef-de-pole", ["member", "unit-head"]),
    ("c0000000-0000-0000-0000-000000000011", "fatou.diallo", "Fatou", "Diallo", "helpdesk", "support", ["member"]),

    # --- DSI / Transformation Digitale (PMO & PO) ----------------------------------------------------------------
    ("c0000000-0000-0000-0000-000000000012", "sebastien.roy", "Sébastien", "Roy", "transfo", "chef-de-pole", ["member", "unit-head"]),
    ("c0000000-0000-0000-0000-000000000009", "nadia.kessler", "Nadia", "Kessler", "transfo", "responsable-pmo", ["member", "pmo"]),
    ("c0000000-0000-0000-0000-000000000013", "claire.moreau", "Claire", "Moreau", "transfo", "product-owner", ["member", "po"]),

    # --- Communication -------------------------------------------------------------------------------------------
    ("c0000000-0000-0000-0000-000000000015", "valerie.lombard", "Valérie", "Lombard", "design", "directeur", ["member", "dept-head"]),
    ("c0000000-0000-0000-0000-000000000016", "hugo.petit", "Hugo", "Petit", "design", "designer", ["member"]),
    ("c0000000-0000-0000-0000-000000000017", "amina.cherif", "Amina", "Cherif", "design", "designer", ["member"]),

    # --- DAF -----------------------------------------------------------------------------------------------------
    ("c0000000-0000-0000-0000-000000000008", "laurent.bouchard", "Laurent", "Bouchard", "controle", "expert-comptable", ["member", "dept-head"]),
    ("c0000000-0000-0000-0000-000000000007", "sofia.navarro", "Sofia", "Navarro", "compta", "comptable", ["member"]),
]

# Every seeded account uses this. Development only — the realm is re-imported on every clean dev box.
SEED_PASSWORD = "cracra"


def attribute_mapper(name, user_attribute, multivalued=False, claim_type="String"):
    """A protocol mapper that lifts a user attribute into the token as a claim.

    The claim names here are the contract UserContextMiddleware reads (see CracraClaims). Renaming one on this side
    without renaming it there produces an authenticated user with no scope — which fails closed, but silently.
    """
    return {
        "name": name,
        "protocol": "openid-connect",
        "protocolMapper": "oidc-usermodel-attribute-mapper",
        "consentRequired": False,
        "config": {
            "user.attribute": user_attribute,
            "claim.name": name,
            "jsonType.label": claim_type,
            "id.token.claim": "true",
            "access.token.claim": "true",
            "userinfo.token.claim": "true",
            "multivalued": "true" if multivalued else "false",
            "aggregate.attrs": "true" if multivalued else "false",
        },
    }


# The claims UserContextMiddleware reads (see CracraClaims), plus the audience the API validates.
CRACRA_MAPPERS = [
    attribute_mapper("unit_id", "unit_id"),
    attribute_mapper("dept_ids", "dept_ids", multivalued=True),
    attribute_mapper("contextual_roles", "contextual_roles", multivalued=True),
    attribute_mapper("functional_role", "functional_role"),
    attribute_mapper("locale", "locale"),
    {
        "name": "audience-cracra-api",
        "protocol": "openid-connect",
        "protocolMapper": "oidc-audience-mapper",
        "consentRequired": False,
        "config": {
            "included.client.audience": "cracra-api",
            "id.token.claim": "false",
            "access.token.claim": "true",
        },
    },
]


def build():
    users = []
    for uid, username, first, last, unit_key, functional_role, contextual_roles in PEOPLE:
        unit_id, _, dept_key, _kind = UNITS[unit_key]
        dept_id, _ = DEPARTMENTS[dept_key]

        users.append({
            "id": uid,
            "username": username,
            "enabled": True,
            "emailVerified": True,
            "firstName": first,
            "lastName": last,
            "email": f"{username}@cracra.local",
            "credentials": [{"type": "password", "value": SEED_PASSWORD, "temporary": False}],
            "attributes": {
                "unit_id": [unit_id],
                "dept_ids": [dept_id],
                "contextual_roles": contextual_roles,
                "functional_role": [functional_role],
                "locale": ["fr"],
            },
            "groups": [f"/{dept_key}/{unit_key}"],
            "realmRoles": ["default-roles-" + REALM],
        })

    # Keycloak represents a service account as a user named service-account-<clientId>. Roles are granted to that
    # user, which is why this lives in "users" rather than on the client: a client scope mapping only bounds which
    # roles a token may carry, it does not grant one.
    #
    # Read-only, deliberately. view-users and the two query roles are everything a reconciliation needs; adding
    # manage-users would make a job that only ever reads capable of rewriting the identity provider it reads from.
    users.append({
        "username": "service-account-cracra-sync",
        "enabled": True,
        "serviceAccountClientId": "cracra-sync",
        "clientRoles": {"realm-management": ["view-users", "query-users", "query-groups"]},
    })

    groups = []
    for dept_key, (dept_id, dept_name) in DEPARTMENTS.items():
        subgroups = []
        for unit_key, (unit_id, unit_name, owner, unit_kind) in UNITS.items():
            if owner == dept_key:
                subgroups.append({
                    "name": unit_key,
                    "path": f"/{dept_key}/{unit_key}",
                    # unit_kind travels as a group attribute because it is a property of the unit, not of anyone
                    # in it. Without it every unit imports as Delivery and a helpdesk reads as a delivery squad.
                    "attributes": {
                        "unit_id": [unit_id],
                        "unit_name": [unit_name],
                        "unit_kind": [unit_kind],
                    },
                    "realmRoles": [],
                    "subGroups": [],
                })

        groups.append({
            "name": dept_key,
            "path": f"/{dept_key}",
            "attributes": {"department_id": [dept_id], "department_name": [dept_name]},
            "realmRoles": [],
            "subGroups": subgroups,
        })

    realm = {
        "realm": REALM,
        "displayName": "Cracra — Activité & Portefeuille",
        "enabled": True,
        "sslRequired": "none",
        "registrationAllowed": False,
        "loginWithEmailAllowed": True,
        "internationalizationEnabled": True,
        "supportedLocales": ["fr", "en", "es"],
        "defaultLocale": "fr",
        "accessTokenLifespan": 900,
        "ssoSessionIdleTimeout": 28800,
        "clients": [
            {
                "clientId": "cracra-bff",
                "name": "Cracra BFF",
                "enabled": True,
                "protocol": "openid-connect",
                # Confidential: the secret lives on the BFF, which is the only thing that ever holds a token.
                "publicClient": False,
                "bearerOnly": False,
                "standardFlowEnabled": True,
                "implicitFlowEnabled": False,
                "directAccessGrantsEnabled": True,
                "serviceAccountsEnabled": False,
                "secret": "cracra-bff-dev-secret",
                # The browser's origin becomes the redirect_uri, and Keycloak matches it literally. 5000 is the
                # BFF serving its own bundle (compose); 4400 is the Angular dev server proxying to the BFF
                # (Aspire dev box). 4200 is deliberately absent — it falls inside a Windows reserved port range.
                "redirectUris": [
                    "http://localhost:5000/*",
                    "http://localhost:4400/*",
                ],
                "webOrigins": ["http://localhost:5000", "http://localhost:4400"],
                "attributes": {
                    "pkce.code.challenge.method": "S256",
                    "post.logout.redirect.uris": "http://localhost:5000/*##http://localhost:4400/*",
                },
                # The mappers live on the client rather than in a shared client scope, and defaultClientScopes is
                # deliberately not set.
                #
                # Declaring a realm-level "clientScopes" array *replaces* Keycloak's built-ins instead of adding to
                # them, so profile/email/roles/web-origins would simply cease to exist — and a client referencing
                # them by name gets those references silently dropped, not an import error. The result is a realm
                # that looks correct in JSON and rejects "openid profile email" at runtime. Omitting the array lets
                # Keycloak create its normal defaults; hanging our five claims off the client keeps them in one
                # place with the redirect URIs they travel with.
                "protocolMappers": CRACRA_MAPPERS,
            },
            {
                "clientId": "cracra-sync",
                "name": "Cracra directory sync",
                "description": "Service account the Directory module reads people and groups with (S1).",
                "enabled": True,
                "protocol": "openid-connect",
                "publicClient": False,
                "bearerOnly": False,
                # Service account only: no interactive flow, so this client can never be used to log a human in.
                "standardFlowEnabled": False,
                "implicitFlowEnabled": False,
                "directAccessGrantsEnabled": False,
                "serviceAccountsEnabled": True,
                "secret": "cracra-sync-dev-secret",
            },
            {
                "clientId": "cracra-api",
                "name": "Cracra API",
                "enabled": True,
                "protocol": "openid-connect",
                # Bearer-only: the API validates tokens and never initiates a login of its own.
                "publicClient": False,
                "bearerOnly": True,
                "standardFlowEnabled": False,
                "directAccessGrantsEnabled": False,
            },
        ],
        "groups": groups,
        "users": users,
        "components": {
        # Keycloak 24+ ships the declarative user profile, and its default policy DISABLES unmanaged attributes.
        # Under that default every custom attribute below — unit_id, dept_ids, contextual_roles — is silently
        # dropped on import, the protocol mappers find nothing to map, and every user authenticates successfully
        # with an empty scope. That fails closed, so nobody sees another department's data; they just see nothing
        # at all, with no error anywhere to explain why.
            "org.keycloak.userprofile.UserProfileProvider": [
                {
                    "providerId": "declarative-user-profile",
                    "config": {
                        "kc.user.profile.config": [
                            json.dumps({
                                "attributes": [
                                    {
                                        "name": "username",
                                        "displayName": "${username}",
                                        "permissions": {"view": ["admin", "user"], "edit": ["admin", "user"]},
                                    },
                                    {
                                        "name": "email",
                                        "displayName": "${email}",
                                        "permissions": {"view": ["admin", "user"], "edit": ["admin", "user"]},
                                    },
                                    {
                                        "name": "firstName",
                                        "displayName": "${firstName}",
                                        "permissions": {"view": ["admin", "user"], "edit": ["admin", "user"]},
                                    },
                                    {
                                        "name": "lastName",
                                        "displayName": "${lastName}",
                                        "permissions": {"view": ["admin", "user"], "edit": ["admin", "user"]},
                                    },
                                ],
                                "unmanagedAttributePolicy": "ENABLED",
                            })
                        ]
                    },
                }
            ],

        # --- LDAP federation ---------------------------------------------------------------------------------------
        # Configured but disabled, per the S0 staging decision. S1 owns the real directory sync; until then the
        # seeded users above are the source, and this block exists so the shape of the mapping is reviewable now
        # rather than invented later. `fonction` is the LDAP attribute that defines a Unit (see the glossary).
            "org.keycloak.storage.UserStorageProvider": [
                {
                    "name": "ldap-directory",
                    "providerId": "ldap",
                    "config": {
                        "enabled": ["false"],
                        "priority": ["0"],
                        "importEnabled": ["true"],
                        "editMode": ["READ_ONLY"],
                        "syncRegistrations": ["false"],
                        "vendor": ["other"],
                        "usernameLDAPAttribute": ["uid"],
                        "rdnLDAPAttribute": ["uid"],
                        "uuidLDAPAttribute": ["entryUUID"],
                        "userObjectClasses": ["inetOrgPerson, organizationalPerson"],
                        "connectionUrl": ["ldap://ldap.internal:389"],
                        "usersDn": ["ou=people,dc=cracra,dc=local"],
                        "authType": ["simple"],
                        "bindDn": ["cn=readonly,dc=cracra,dc=local"],
                        "searchScope": ["2"],
                        "fullSyncPeriod": ["3600"],
                        "changedSyncPeriod": ["600"],
                    },
                }
            ]
        },
    }

    return realm


if __name__ == "__main__":
    output = pathlib.Path(__file__).with_name("cracra-realm.json")
    output.write_text(json.dumps(build(), indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"wrote {output} ({output.stat().st_size} bytes)")
