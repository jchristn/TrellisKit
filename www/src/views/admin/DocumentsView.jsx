import { useMemo } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import DocumentsManager from '../../components/documents/DocumentsManager.jsx';

const route = strings.admin.routes.documents;

/** The documents console over the admin routes (`/api/v1/admin/documents...`) of a client. */
export function adminDocumentsSource(client) {
  return {
    list: (query, signal) => client.adminListDocuments(query, signal),
    get: (key, signal) => client.adminGetDocument(key, signal),
    enumerateVersions: (key, query, signal) => client.adminEnumerateDocumentVersions(key, query, signal),
    getVersion: (key, versionId, signal) => client.adminGetDocumentVersion(key, versionId, signal),
    publish: (key, body) => client.adminPublishDocumentVersion(key, body),
    enumerateAcceptances: (query, signal) => client.adminEnumerateDocumentAcceptances(query, signal),
    metadataClient: client,
    publicLinks: true
  };
}

/**
 * /admin/documents (SystemAdmin): the property's required documents (terms, policies, waivers),
 * their versions and publishing, and who accepted which version.
 */
function DocumentsView() {
  const { client } = useAdminAuth();
  const source = useMemo(() => adminDocumentsSource(client), [client]);
  return (
    <div className="view documents-view">
      <PageHeader title={route.label} summary={route.summary} />
      <DocumentsManager source={source} />
    </div>
  );
}

export default DocumentsView;
