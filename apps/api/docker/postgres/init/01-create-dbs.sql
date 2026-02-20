-- Create hangfire database if it doesn't exist (Postgres doesn't support IF NOT EXISTS for CREATE DATABASE)
SELECT 'CREATE DATABASE hangfire OWNER woopulse'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'hangfire')\gexec
