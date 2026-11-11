# 💡 Full-Stack Exercise – Smart Customer Support System

## 🏁 Introduction
This exercise is designed to assess your **Full-Stack development skills** using modern technologies.

You are asked to build a **simple yet functional customer-support ticket system**, demonstrating your understanding of:
- Frontend and backend integration  
- Data persistence  
- Clean and maintainable code structure  

You may use the **internet** and official **documentation** freely.  
You may also use **AI tools** (such as ChatGPT or Gemini) — **as long as you can explain every line of code** in your project.

> ⚙️ **Note:** The backend should preferably be implemented using **ASP.NET Core Minimal API**.

---

## 🎯 Objective
Build a system that allows Customers to:
- **create new support tickets**
- **view and edit existing tickets**

---

## 🧰 Required Technologies

- **Frontend:** React  
- **Backend:** ASP.NET Core Web API *(preferably using Minimal API)*  
- **Data Storage:** JSON file (provided)  
  - All read/write operations must be done on the **server-side only**
  - The frontend should communicate **only via API endpoints**
  - The backend should include **Entities**, **DTOs**, and **Service Classes**

### ✅ Bonus Features
- **Admin Login screen** - only logged users can edit tickets, all users can add new tickets.
- **JWT Authentication**
- **AI Summary Generation** - using OpenAI / Gemini API
- **Email Sending**

---

## 📋 Functional Requirements

### 🛠️ Tickets View Screen

**Features:**
- Display all tickets in a **table view**
- Filter by:
  - **Status** (dropdown)
  - **Text search** (by name or description)
- Show **full description** and **AI summary** *(if available)*
- Updated **status**
- Click to access single ticket view.
- New Ticket Button that opens Create New Ticket modal with Fields:
  - Full Name
  - Email Address
  - Issue Description  

**On Submit:**
- Save the ticket data in the JSON file  
- Simulate sending an **email to the customer** with a tracking link  

**Bonus:**  
While saving the ticket, call an **AI API** to generate and display a short **summary** of the issue.

---

### 🔍 Detailed Ticket View Screen *(Accessible by Unique ID)*

- Choose your preferred approach for the detailed view (modal, side panel, or separate route)
- It has to be accessed by a **unique ticket ID** (e.g., `/tickets/{id}`).

**Displayed Information:**
- Ticket Uniaue Id
- Customer details  
- Issue description  
- AI-generated summary *(if available)*  
- Current status
	- edit status (dropdown)
- Resolution text *(if available)*
	- Add or edit **resolution text**
- **Save changes**

---

## 📬 Email Notifications

All emails can be sent using **any of the following options**:
- **Mock email service** that simulates email sending (for example, logs messages to the console or stores them in memory)
**Bonus:**
- **Gmail SMTP**, using your personal Gmail account
- **Alternative email service implementation** (e.g., SendGrid, MailKit, AWS SES)  

Simulate sending an email to the customer in the following cases:
1. After creating a new ticket  
2. Whenever the **status** changes  
3. Whenever the **resolution text** changes  

---

## ✅ Bonus Features Summary

| Feature | Description |
|----------|--------------|
| **Authentication (User&Password / JWT)** | Login & role-based authorization for admin access |
| **AI Summary** | Generate a short summary for the issue using an AI API |
| **UI/UX Design** | Clean, responsive, and accessible interface |
| **Email Sending** | Generic integration with email service |

---

## 📸 Example Screens

The system includes the following key screens:

1. **All Tickets View** – for customers to see existing issues  
2. **Single Ticket View** – detailed view by unique ID
3. **Login Screen** – for authentication *(bonus)*  

### 🖼️ Screenshots

| Login | New Ticket | Ticket Management (Admin) |
|:------:|:-----------:|:----------------:|
| ![Login Screenshot](./login.png#gh-light-mode-only) | ![New Ticket Screenshot](./new-ticket.png#gh-light-mode-only) | ![Ticket Management Screenshot](./ticket-manage.png#gh-light-mode-only) |

*(Use your own screenshots here or replace with your actual UI captures.)*

---

## 🚀 Submission

Please submit your solution as a **GitHub repository link** containing both:
- The **React frontend**
- The **.NET8/9 backend**

---

## 🧾 Evaluation Criteria

| Category | Description |
|-----------|-------------|
| **Functionality** | Meets all required features and flows |
| **Code Quality** | Clean, modular, and maintainable |
| **Architecture** | Proper separation of frontend, backend, and services |
| **UI/UX** | User-friendly, clear, and responsive design |
| **Bonus Features** | Authentication and AI integration implemented |

---

🧠 *Good luck and have fun building!*
