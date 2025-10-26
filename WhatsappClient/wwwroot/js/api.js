const API = {
    async getContacts() {
        const url = `${window.API_BASE}/api/general/contacto`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return await res.json();
    },

    async getContactById(id) {
        const url = `${window.API_BASE}/api/general/contacto/${id}`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return await res.json();
    },

    async upsertContact(payload) {
        const url = `${window.API_BASE}/api/general/contacto/upsert`;
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        const json = await res.json();
        if (!res.ok) throw new Error(json?.mensaje ?? `HTTP ${res.status}`);
        return json;
    },

    async deleteContact(id) {
        const url = `${window.API_BASE}/api/general/contacto/Delete/${id}`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return await res.json();
    }
};
