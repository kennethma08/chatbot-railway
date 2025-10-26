document.addEventListener('DOMContentLoaded', () => {
    const tbody = document.querySelector('#tbl-contacts tbody');
    const btnRefresh = document.getElementById('btn-refresh');
    const btnNew = document.getElementById('btn-new');
    const modal = document.getElementById('contact-modal');
    const btnCancel = document.getElementById('btn-cancel');
    const frm = document.getElementById('frm-contact');


    // Cargar los contactos desde la API
    async function loadContacts() {
        try {
            const resp = await API.getContacts(); // Obtener los datos desde la API
            const list = resp?.data?.$values ?? []; // Asegurarse de acceder a los valores correctos

            tbody.innerHTML = ''; // Limpiar la tabla

            list.forEach(contact => {
                const id = contact.id;
                const name = contact.name;
                const phone = contact.phoneNumber;
                const email = contact.email ?? '';  // Asegúrate de que 'email' esté disponible

                const tr = document.createElement('tr');
                tr.innerHTML = `
                    <td>${id}</td>
                    <td>${name}</td>
                    <td>${phone}</td>
                    <td>${email}</td>
                    <td>
                        <button class="btn-edit btn btn-sm btn-outline-primary" data-id="${id}">Ver</button>
                    </td>
                `;
                tbody.appendChild(tr);
            });
        } catch (err) {
            console.error(err);
            alert('Error cargando contactos: ' + err.message);
        }
    }

    // Manejo de los botones dentro de la tabla
    tbody.addEventListener('click', async (ev) => {
        const id = ev.target.dataset.id;
        if (!id) return;

        // Eliminar contacto
        if (ev.target.classList.contains('btn-del')) {
            if (!confirm('¿Eliminar contacto ' + id + '?')) return;
            await API.deleteContact(Number(id));
            await loadContacts();
        }

        // Editar contacto
        if (ev.target.classList.contains('btn-edit')) {
            const data = await API.getContactById(Number(id));
            const contact = data?.data ?? data;

            // Llenar el formulario con los datos del contacto
            document.getElementById('contactId').value = contact.id ?? '';
            document.getElementById('name').value = contact.name ?? '';
            document.getElementById('phone').value = contact.phoneNumber ?? '';
            document.getElementById('email').value = contact.email ?? '';
            document.getElementById('notes').value = contact.notes ?? '';
            document.getElementById('welcomeSent').value = contact.welcomeSent ? 'true' : 'false'; // Asegurar que WelcomeSent esté correctamente establecido
            document.getElementById('country').value = contact.country ?? ''; // Aseguramos que el campo "País" esté correctamente establecido
            document.getElementById('ipAddress').value = contact.ipAddress ?? ''; // Aseguramos que el campo "Dirección IP" esté correctamente establecido
            document.getElementById('profilePic').value = contact.profilePic ?? ''; // Aseguramos que el campo "Foto de Perfil" esté correctamente establecido
            document.getElementById('status').value = contact.status ?? ''; // Aseguramos que el campo "Estado" esté correctamente establecido

            openModal('Editar contacto');
        }
    });

    // Refrescar la lista de contactos
    btnRefresh.addEventListener('click', loadContacts);

    // Cargar los contactos cuando se carga la página
    loadContacts();
});
