// PZP-23: заказчик заказа, если заказ привязан; иначе компания типа изделия.
// Пустой заказчик привязанного заказа не заменяется компанией типа.
export function taskCustomer(task) {
  if (!task) return null
  return (task.orderId ? task.orderCustomer : task.productTypeCompany) || null
}

export function hasTaskCustomerMismatch(task) {
  const orderCustomer = task?.orderCustomer?.trim()
  const typeCompany = task?.productTypeCompany?.trim()
  return !!task?.orderId && !!orderCustomer && !!typeCompany
    && orderCustomer.toLowerCase() !== typeCompany.toLowerCase()
}
