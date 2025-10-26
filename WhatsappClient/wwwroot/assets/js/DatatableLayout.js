(function ($) {
    $(document).ready(function () {
        if (typeof $.fn.DataTable === 'undefined') {
            console.warn('DataTables no está cargado.');
            return;
        }

        var $tbl = $('#example');
        if ($tbl.length) {
            $tbl.DataTable({
                responsive: true,
                language: { url: "//cdn.datatables.net/plug-ins/1.13.6/i18n/es-ES.json" },
                layout: {
                    topStart: {
                        buttons: ['copy', 'csv', 'excel', 'pdf', 'print']
                    }
                }
            });
        }
    });
})(jQuery);
