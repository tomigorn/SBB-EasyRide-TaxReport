// Helper function to download files from base64 data
window.downloadFile = function (filename, base64Data) {
    const link = document.createElement('a');
    link.download = filename;
    link.href = 'data:text/csv;base64,' + base64Data;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
