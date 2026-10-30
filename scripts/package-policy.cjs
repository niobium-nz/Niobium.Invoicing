const CONTRACTS = 'src/Niobium.Invoicing.Contracts/';

// GitHub reports a rename's destination in filename and its source in previous_filename.
function contractsChanged(files) {
  return files.some(file => [file.filename, file.previous_filename]
    .some(path => typeof path === 'string' && path.startsWith(CONTRACTS)));
}

module.exports = { contractsChanged };
