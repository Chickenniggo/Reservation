SELECT pg_advisory_xact_lock(742349120);
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE TABLE IF NOT EXISTS reservations (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    comment text,
    "from" date NOT NULL,
    "to" date NOT NULL,
    room_id uuid NOT NULL,
    deleted_at timestamptz,
    version uuid NOT NULL DEFAULT gen_random_uuid()
);
ALTER TABLE reservations ADD COLUMN IF NOT EXISTS version uuid NOT NULL DEFAULT gen_random_uuid();
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'reservations_valid_dates' AND conrelid = 'reservations'::regclass) THEN
        ALTER TABLE reservations ADD CONSTRAINT reservations_valid_dates CHECK ("from" < "to");
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'reservations_no_overlap' AND conrelid = 'reservations'::regclass) THEN
        ALTER TABLE reservations ADD CONSTRAINT reservations_no_overlap
            EXCLUDE USING gist (room_id WITH =, daterange("from", "to", '[)') WITH &&)
            WHERE (deleted_at IS NULL);
    END IF;
END $$;