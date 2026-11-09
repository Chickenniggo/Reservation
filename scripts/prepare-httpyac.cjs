const fs = require('node:fs');
const path = require('node:path');

const directory = path.join(process.argv[2], 'httpyac');
const configurationPath = path.join(directory, 'httpyac.config.ts');
const configuration = fs.readFileSync(configurationPath, 'utf8');
const originalDirectories = "path.join(__dirname, '..', 'specs', 'v3')];";
if (configuration.includes(originalDirectories)) {
  fs.writeFileSync(configurationPath, configuration.replace(originalDirectories,
    "path.join(__dirname, '..', 'specs', 'v3'), path.join(__dirname, '..', 'specs', 'v4')];"));
} else if (!/^const OAS_DIRS.*['"]v4['"]/m.test(configuration)) {
  throw new Error('The pinned httpyac schema configuration has changed.');
}

const environmentPath = path.join(directory, 'httpyac.env.json5');
const environment = fs.readFileSync(environmentPath, 'utf8');
const originalUrl = '"reservations-api-v3": "/api/v3/reservations",';
if (environment.includes(originalUrl)) {
  fs.writeFileSync(environmentPath, environment.replace(originalUrl,
    '"reservations-api-v4": "/api/v4/reservations",'));
} else if (!environment.includes('"reservations-api-v4": "/api/v4/reservations",')) {
  throw new Error('The pinned httpyac environment configuration has changed.');
}