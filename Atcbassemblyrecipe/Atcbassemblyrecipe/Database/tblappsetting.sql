-- =====================================================================
-- UI settings - RUN THIS ONCE against the OCAPSYS schema.
--
-- Every value the pages used to hard-code lives here: the product name,
-- the logo and background images, the login wording, the brand colours
-- and the animation timings. Changing a row and reloading the page is
-- enough - no rebuild, no redeploy.
--
-- Keys are (MODULE_NAME, SETTING_KEY). MODULE_NAME groups the settings the
-- way the app is laid out, so one module can be re-skinned without touching
-- the others:
--
--   APP     product name, browser title suffix, favicon
--   LOGIN   the sign-in page: logos, headline, kicker, background
--   LAYOUT  the signed-in shell: brand lockup, background
--   THEME   design tokens - colours and animation timings, light and dark
--
-- SETTING_TYPE says what the value is, so an editor can offer the right
-- control later: TEXT, URL, IMAGE, VIDEO, COLOR, NUMBER, BOOLEAN, CSS.
--
-- The app reads this table through Data/AppSettingRepository.cs and caches
-- it for AppSettings:CacheSeconds (appsettings.json, default 60). Until this
-- script has been run - or if the table is unreachable - the app falls back
-- to the same values held in Models/AppSettingDefaults.cs, so nothing breaks;
-- the start-up log says which of the two is in use.
-- =====================================================================

CREATE TABLE tblappsetting (
    setting_id    NUMBER GENERATED ALWAYS AS IDENTITY,
    module_name   VARCHAR2(100) NOT NULL,
    setting_key   VARCHAR2(100) NOT NULL,
    setting_value VARCHAR2(2000),
    setting_type  VARCHAR2(20) DEFAULT 'TEXT' NOT NULL,
    description   VARCHAR2(400),
    is_active     CHAR(1) DEFAULT 'Y' NOT NULL,
    updated_by    VARCHAR2(50),
    updated_date  DATE DEFAULT SYSDATE NOT NULL,
    created_date  DATE DEFAULT SYSDATE NOT NULL,
    CONSTRAINT pk_tblappsetting PRIMARY KEY (setting_id),
    CONSTRAINT uq_tblappsetting_key UNIQUE (module_name, setting_key),
    CONSTRAINT ck_tblappsetting_active CHECK (is_active IN ('Y', 'N')),
    CONSTRAINT ck_tblappsetting_type
        CHECK (setting_type IN ('TEXT', 'URL', 'IMAGE', 'VIDEO', 'COLOR',
                                'NUMBER', 'BOOLEAN', 'CSS'))
);

CREATE INDEX ix_tblappsetting_module ON tblappsetting (module_name, is_active);

-- ---------------------------------------------------------------------
-- APP
-- ---------------------------------------------------------------------
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('APP', 'ProductName', 'ATCB Assembly Recipe', 'TEXT', 'Product name in the browser title and the brand lockup', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('APP', 'TitleSuffix', 'ATCB Assembly Recipe', 'TEXT', 'Text after the page title in the browser tab', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('APP', 'FaviconUrl', '~/images/app-icon.png', 'IMAGE', 'Browser tab icon', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('APP', 'CompanyName', 'Nexperia', 'TEXT', 'Company name used as the logo alt text', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('APP', 'EmailSubjectPrefix', '[ATCB Recipe]', 'TEXT', 'Prefix on notification email subjects', 'SEED');

-- ---------------------------------------------------------------------
-- LOGIN
-- ---------------------------------------------------------------------
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'LogoUrl', '~/images/nexperia-logo.png', 'IMAGE', 'Logo on the left panel of the sign-in page', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'FormLogoUrl', '~/images/nexperia-logo.png', 'IMAGE', 'Small logo above the sign-in form', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'Headline', 'ATCB Assembly Recipe', 'TEXT', 'Large headline on the sign-in page', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'Kicker', 'EFFICIENCY WINS.', 'TEXT', 'Small caps line above Sign In', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'FormTitle', 'Sign In', 'TEXT', 'Heading of the sign-in form', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'SubmitText', 'Sign In', 'TEXT', 'Text on the sign-in button', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'SubmitBusyText', 'Signing In', 'TEXT', 'Text on the sign-in button while the form is posting', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'UserPlaceholder', 'Nexperia account', 'TEXT', 'Placeholder in the username box', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'PasswordPlaceholder', 'Password', 'TEXT', 'Placeholder in the password box', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'BackgroundImageUrl', '~/images/atf-cabuyao.jpg', 'IMAGE', 'Photo behind the sign-in page', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LOGIN', 'BackgroundVideoUrl', '', 'VIDEO', 'Optional looping video behind the sign-in page. Set a URL to use a video instead of the photo', 'SEED');

-- ---------------------------------------------------------------------
-- LAYOUT
-- ---------------------------------------------------------------------
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LAYOUT', 'LogoUrl', '~/images/nexperia-logo.png', 'IMAGE', 'Logo in the top bar', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LAYOUT', 'BrandText', 'ATCB Assembly Recipe', 'TEXT', 'Brand text next to the logo in the top bar', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('LAYOUT', 'BackgroundImageUrl', '~/images/atf-cabuyao.jpg', 'IMAGE', 'Photo behind the signed-in shell', 'SEED');

-- ---------------------------------------------------------------------
-- THEME - design tokens. These are written into :root as CSS variables, so
-- they override the defaults in wwwroot/css/site.css without editing the CSS.
-- ---------------------------------------------------------------------
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'nxp-teal', '#007c84', 'COLOR', 'Primary brand colour', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'nxp-teal-dark', '#00636a', 'COLOR', 'Primary colour, pressed and hover', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'nxp-teal-soft', '#e4f3f4', 'COLOR', 'Primary tint for panels', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'nxp-orange', '#ff4f26', 'COLOR', 'Accent colour, primary buttons', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'nxp-orange-dark', '#e43d17', 'COLOR', 'Accent colour, pressed and hover', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'ink', '#202837', 'COLOR', 'Body text', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'muted', '#667085', 'COLOR', 'Secondary text', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'line', '#d8e1e8', 'COLOR', 'Borders and rules', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'surface', '#ffffff', 'COLOR', 'Card and panel background', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'canvas', '#f3f7fa', 'COLOR', 'Page background', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'success', '#12a17a', 'COLOR', 'Success state', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'warning', '#f2a33a', 'COLOR', 'Warning state', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'danger', '#e83749', 'COLOR', 'Error state', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'motion-fast', '140ms', 'TEXT', 'Animation length for hovers and small state changes', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'motion-base', '240ms', 'TEXT', 'Animation length for panels, menus and the sidebar', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'motion-slow', '480ms', 'TEXT', 'Animation length for page and modal entrances', 'SEED');
INSERT INTO tblappsetting (module_name, setting_key, setting_value, setting_type, description, updated_by)
VALUES ('THEME', 'motion-ease', 'cubic-bezier(0.22, 0.61, 0.36, 1)', 'TEXT', 'Easing curve shared by every animation', 'SEED');

COMMIT;

-- Quick check that it worked:
-- SELECT module_name, setting_key, setting_value FROM tblappsetting ORDER BY module_name, setting_key;
