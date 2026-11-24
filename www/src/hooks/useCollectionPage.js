import { useMemo, useState } from 'react';

/** Independent page controls for a server-enumerated catalog or history table. */
export default function useCollectionPage(initialSize = 25) {
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialSize);
  const [result, setResult] = useState(null);
  const query = useMemo(() => ({ maxResults: pageSize, skip: (pageNumber - 1) * pageSize }), [pageNumber, pageSize]);
  return {
    query, setResult,
    props: {
      pageNumber, pageSize, totalCount: result?.totalRecords || 0,
      totalPages: Math.ceil((result?.totalRecords || 0) / pageSize),
      onPageChange: setPageNumber,
      onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); }
    }
  };
}
